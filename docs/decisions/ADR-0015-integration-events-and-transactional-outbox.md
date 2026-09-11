# ADR-0015 — Integration events and the transactional outbox

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Related:** ADR-0006 (modular monolith), ADR-0007 §10.3, ADR-0013 (webhooks), ADR-0014 (jobs)

## Context

Modules in the same tier communicate by event (`../architecture/modules.md` §2), webhooks must be delivered to third parties (ADR-0013), and a module must remain extractable (ADR-0006). All three need the same guarantee: **an event is published if and only if the business transaction that produced it committed.**

The naive implementation — commit the transaction, then publish — loses events whenever the process dies in between, and publishes phantom events whenever the publish succeeds and the commit does not. In a ledger, a lost "invoice posted" event means an AR open item that never exists.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Dual write (commit, then publish) | Trivial | Loses or duplicates events on any crash between the two. Unacceptable for financial events |
| Two-phase commit across database and broker | Atomic in theory | XA with PostgreSQL and a broker is operationally miserable, blocks on coordinator failure, and multiplies by the number of tenant databases. Rejected |
| **Transactional outbox in the tenant database + a dispatcher** *(chosen)* | The event row and the aggregate change commit in **one** local transaction — no distributed transaction, no loss; the outbox is per tenant, so it is isolated, backed up and deleted with the tenant | At-least-once delivery, so every consumer must be idempotent; the dispatcher is code we own |
| Change data capture (Debezium / logical replication) | No application code; captures everything | A publication and connector per tenant database — thousands — plus Kafka. The operational cost dwarfs the problem |
| In-process event bus only | Simplest | Not durable: a crash after commit and before handling loses the event silently. Fine for domain events inside one transaction, fatal for integration events |

## Decision

**Two kinds of event, and they must never be confused.**

### Domain events — in-process, inside the transaction

Raised by an aggregate, handled **before commit**, in the same transaction, within one module. They keep a module's own model consistent (`InvoiceLineAdded` recalculates the document total). They are not durable, not observable outside the module, and never cross a module boundary.

### Integration events — always through the outbox

Everything that crosses a module boundary, reaches a webhook, or might one day cross a process boundary.

1. **Written to `platform.outbox` in the tenant database, in the same transaction as the aggregate change.** No exceptions; a fitness test asserts no integration-event publish happens outside an active transaction.
2. Envelope: `message_id` (UUIDv7), `tenant_id`, `type`, `version`, `ordering_key`, `occurred_at`, `correlation_id`, `causation_id`, `payload` (JSON).
3. **The dispatcher is the only publisher.** In-process "dispatch after commit" exists solely as a low-latency *signal that the outbox has work* (ADR-0007 §10.3) — never as the delivery mechanism. If the process dies, the sweep delivers it. This distinction is the whole point of the pattern and is the thing teams most often get wrong.
4. **Delivery is at-least-once; every consumer is idempotent**, backed by an inbox table with a unique constraint on `(message_id, handler)`. A duplicate is a no-op, not an error.
5. **Ordering is per `ordering_key` only** (normally the aggregate id). Global ordering is not offered, because providing it would serialise the dispatcher.
6. **Cross-tenant events do not exist.** One envelope, one `tenant_id`, asserted by the dispatcher before a handler runs (ADR-0007 §10.4).
7. **Poison handling:** exponential backoff, `max_attempts` 8, then dead-letter with an alert. A dead-lettered financial event is an incident, not a statistic.
8. **Schema evolution is additive.** Adding an optional field is fine; anything else is a new event type (`SalesInvoicePostedV2`) published alongside the old one for one deprecation window. Event contracts live in `.Contracts` assemblies and are covered by the approved-API snapshot test, so a breaking change cannot happen by accident.
9. **Dispatched rows are pruned after 7 days** by a per-tenant maintenance job; the audit trail of what happened lives in `audit`, not in the outbox.
10. **Webhook delivery** is a dispatcher subscriber: it reads the same `message_id`, so a consumer can deduplicate across retries (ADR-0013 §9).

**No message broker at bootstrap.** The dispatcher delivers in-process to module handlers and over HTTP to webhooks. Introducing a broker later changes the dispatcher's transport and nothing else, because the outbox — not the broker — is the durability boundary. That is deliberate: with database-per-tenant, a broker would have to be partitioned by tenant anyway, and we would gain queueing we do not yet need in exchange for a service we would have to operate.

## Consequences

- Positive: no event is ever lost or phantom-published, without a distributed transaction.
- Positive: the outbox lives with the tenant, so backup, restore, export and deletion need no special case.
- Positive: idempotent consumers plus at-least-once semantics mean extracting a module later does not change the semantics its consumers already handle (ADR-0006).
- Negative: every consumer must be written idempotently, and "idempotent" is easy to claim and easy to get wrong. Each handler has an explicit duplicate-delivery test.
- Negative: eventual consistency between modules. A posted invoice's AR open item appears milliseconds later, not instantly. The UI must not show a state that implies otherwise; where a user needs immediate confirmation, the read comes from the producing module.
- Negative: the dispatcher is infrastructure we own and must monitor. Outbox lag (oldest undispatched row age) is a first-class alert (ADR-0016).
- Negative: at-least-once plus per-key ordering is weaker than exactly-once and global ordering, which some developers will assume. It is stated in the contract assembly's XML docs on `IIntegrationEvent`, where it is unavoidable.

## Revisit when

Fan-out to external consumers outgrows in-process HTTP delivery, a module is actually extracted (ADR-0006), or outbox lag becomes a recurring alert at realistic load — at which point the transport becomes a broker while the outbox stays exactly as it is.
