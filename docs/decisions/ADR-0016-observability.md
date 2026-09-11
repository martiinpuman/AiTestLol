# ADR-0016 — Observability

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Related:** ADR-0007 (tenancy), ADR-0018 (audit and personal data)

## Context

`CLAUDE.md` requires structured logs with tenant and correlation IDs, and **no personal data in logs**. ADR-0007 adds a harder problem: with thousands of tenant databases, the questions operators actually ask are fleet-shaped — *which tenants are failing, which are slow, how far behind is the outbox, is a migration wave stuck* — and none of them can be answered by a single SQL query.

Observability is also where a multi-tenant system quietly acquires two self-inflicted wounds: personal data leaking into logs, and per-tenant metric labels exploding cardinality.

## Options considered

| Option | Licence (verified 2026-09-11) | Pros | Cons |
|---|---|---|---|
| **OpenTelemetry for traces and metrics + Serilog as the logging provider, both behind `ILogger<T>`** *(chosen)* | OpenTelemetry .NET 1.18.0 **Apache-2.0**; Serilog.AspNetCore 10.0.0 **Apache-2.0** | Vendor-neutral wire format (OTLP) so the backend is a deployment decision, not an architectural one; Serilog gives mature structured sinks and enrichment; `ILogger<T>` in application code means no library type appears in our source | Two libraries where one might do; OTLP semantic conventions keep evolving |
| `Microsoft.Extensions.Logging` + OpenTelemetry only | MIT + Apache-2.0 | One fewer dependency | Loses Serilog's enrichment and sink ecosystem, both of which we would end up rebuilding |
| A vendor agent (Datadog, New Relic, Application Insights) | Commercial / cloud | Least setup | Costs money and requires a sign-up — a hard limit. Also couples telemetry to one vendor |
| Logs only, no traces | Trivial | A posting that crosses five modules, an outbox hop and a job is unreconstructable from logs |

## Decision

### 1. Three signals, one correlation

- **Logs** via `ILogger<T>` (no Serilog types in application code), structured, rendered as JSON, exported over OTLP.
- **Traces** via OpenTelemetry: ASP.NET Core, HttpClient, EF Core/Npgsql, plus explicit spans around posting, tax determination, package install, provisioning and migration.
- **Metrics** via `System.Diagnostics.Metrics`, exported over OTLP.
- Every log line and span carries `trace_id` and `span_id`; a `correlation_id` propagates across the outbox and into jobs via the event envelope (ADR-0015), so a webhook delivery traces back to the click that caused it.

### 2. Mandatory enrichment

`tenant.id`, `tenant.key`, `company.id`, `user.id` (the surrogate id — **never** an email), `correlation.id`, `request.id` and, for jobs, `job.type` and `job.id`. Applied centrally by the tenant middleware, the circuit handler and the job dispatcher — never by hand at a call site, because hand-applied context is missing exactly where the incident is.

### 3. No personal data in logs, enforced not requested

- Properties annotated `[PersonalData]` (ADR-0018) are redacted by a logging redaction pipeline before a sink sees them.
- A fitness test fails the build when a `[PersonalData]` property is passed to a log message template.
- Structured logging only: `logger.LogInformation("Posted invoice {InvoiceId}", id)`. String interpolation into a log message is banned by an analyzer, because interpolation defeats both redaction and indexing.
- Exception messages from a database driver can contain row values; SQL parameter logging (`EnableSensitiveDataLogging`) is off, and a startup assertion fails if it is ever on outside Development.

### 4. Metric cardinality — the trap this architecture sets

Tagging metrics with `tenant.id` produces, at 10 000 tenants and a handful of metrics, millions of time series, which will bankrupt any metrics backend. Therefore:

- **`tenant.id` appears on logs and traces (high cardinality is normal there) and NOT as a metric dimension.**
- Metrics are dimensioned by `tenant.tier` (a small, bounded set) and by endpoint, module and outcome.
- Per-tenant numbers come from traces and from a nightly fan-out job writing into catalog summary tables — the same pattern as all other fleet reporting under ADR-0007.

### 5. The metrics that matter here

RED (rate, errors, duration) per endpoint and per application service, plus the ones specific to this architecture:

| Metric | Why it exists |
|---|---|
| Npgsql pool: busy/idle connections per cluster | Bottleneck #1 (`../architecture/scalability.md` §4). If we watch one thing, it is this |
| Active data sources per process | Detects the LRU cache thrashing (ADR-0007 §5.1) |
| Outbox lag: age of the oldest undispatched row, per tenant tier | Silent event loss looks exactly like "everything is fine" without this |
| Job queue depth and oldest ready item | Backlog before users notice |
| Migration run progress, quarantined tenant count | The fleet operation most likely to go wrong |
| Blazor circuits: active, disconnected, memory per circuit | The server-memory ceiling in `scalability.md` §3 |
| Tenant scope opens by result (ok / schema-blocked / routing-violation) | A routing violation is a sev-1; it must be a metric, not a log line someone might read |
| Posting latency and failed postings | Quality attribute #1 |

### 6. Health checks that do not fan out

- **Liveness:** the process is up. No I/O.
- **Readiness:** the catalog is reachable and this instance's already-warm tenant data sources respond.
- **Never** a health check that touches every tenant database — that turns a Kubernetes probe into a fleet-wide connection storm, which is a self-inflicted outage of a particularly embarrassing kind.
- Per-tenant health is a scheduled fan-out job writing to the catalog, surfaced on the operator console.

### 7. Sampling and retention

Parent-based sampling at 10%, with **always-sample** for errors, financial postings, package installs, provisioning and migration runs. Traces retained 7 days, logs 30 days, metrics 13 months. Retention is stated here because "we have the telemetry" is worthless if it aged out before the customer reported the problem.

## Consequences

- Positive: OTLP means the backend is swappable and nothing in the codebase names a vendor.
- Positive: enforced redaction plus the fitness test makes "no personal data in logs" a build outcome rather than a reviewer's vigilance.
- Positive: the cardinality rule is decided before the first dashboard exists, which is the only time it is cheap to decide.
- Negative: per-tenant metrics require a fan-out job. Answering "how slow is tenant 42?" costs a trace query rather than a dashboard filter. That is the price of not exploding the metrics backend.
- Negative: instrumentation is code that must be written and reviewed like any other, and under-instrumented modules are invisible until an incident.

## Revisit when

Trace volume or cost forces a lower sampling rate, an incident is not diagnosable from the current signals (add the missing one and record why), or OpenTelemetry's .NET logging API changes materially.
