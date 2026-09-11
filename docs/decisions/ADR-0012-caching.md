# ADR-0012 — Caching

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Related:** ADR-0007 (tenancy), ADR-0011 (configuration)

## Context

Some reads happen on every request and change rarely: tenant routing (ADR-0007 §3.5), the resolved permission set (ADR-0010), the installed-package set, account-role mappings, localization resources. Without caching, every request pays a catalog round trip and the catalog becomes the bottleneck the whole fleet shares.

The danger is specific and severe: **a cache is a place where tenant data can leak without any database being involved.** A cache key without a tenant prefix serves tenant A's data to tenant B, and no isolation test that only looks at databases will catch it.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| No caching | Nothing to invalidate; no leak surface | The catalog is read on every request; ADR-0007 §9.4 becomes untenable |
| `IMemoryCache` per instance | Trivial, in-box | No cross-instance invalidation; each instance holds its own copy; no stampede protection |
| **`HybridCache` (`Microsoft.Extensions.Caching.Hybrid` 10.10.0, MIT, verified 2026-09-11)** *(chosen)* | In-box, L1 in-memory with an optional L2 distributed backend behind the same API; **stampede protection built in** (concurrent misses collapse to one factory call); tag-based invalidation; adding L2 later is configuration, not a rewrite | Newer than `IMemoryCache`; the L2 story needs a distributed store we do not yet run |
| Redis only | Shared across instances from day one | An extra service for a single-instance bootstrap; and Redis's own licence changed to RSALv2/SSPL in 2024, which fails `CLAUDE.md`'s permissive-licence rule |

## Decision

**`HybridCache`, in-memory only (L1) at bootstrap, with the L2 seam left open.** When multiple instances make a distributed tier necessary, the backend is **Valkey** (BSD-3-Clause, the community fork of Redis) and not Redis — recorded now so the licence question is settled before someone reaches for the obvious package under deadline.

Binding rules:

1. **Every cache key is built by `TenantCacheKey.For(scope, ...)`, which prefixes `t:{tenantId}:`.** Raw string keys are banned, and a fitness test asserts no call to a `HybridCache` method uses a key not produced by that helper. This is the cache equivalent of ADR-0007's structural guarantee, and it exists because the alternative — remembering — fails eventually.
2. **Catalog-level entries** (tenant routing, feature flags, package catalogue) are prefixed `c:` and explicitly marked as cross-tenant. There are few of them and they hold no business data.
3. **What is cached, and for how long:**

| Entry | TTL | Invalidated by |
|---|---|---|
| Tenant routing / connection | 60 s | Tenant state change, cluster move |
| Permission set per (tenant, user) | 60 s | Role or assignment change |
| Installed package set | 5 min | Install, upgrade, deactivate |
| Account-role mapping | 5 min | Chart of accounts change |
| Localization resources | 30 min | Package install, locale override change |
| Feature flag definitions | 60 s | Flag change |

4. **What is never cached:** ledger balances, document totals, stock quantities, authorization *decisions* (as opposed to permission sets), anything containing personal data, and anything a user is about to act on financially. A stale stock figure that causes an oversell is a business error, not a performance optimisation.
5. **Cache-aside only.** No write-through, no cache as a store of record. If the cache is empty, the system is correct and slower; that must always be true.
6. **Entries are evicted on tenant suspend, migration and offboarding** — a suspended tenant must not keep being served from a warm cache.

## Consequences

- Positive: the catalog stops being read on every request; a brief catalog outage degrades to serving already-cached tenants (ADR-0007 §9.4).
- Positive: stampede protection means a cold cache after a deployment does not produce a thundering herd against the catalog — the failure mode `IMemoryCache` would have given us.
- Negative: bounded staleness windows are now part of the security story (a revoked permission lives up to 60 s). The numbers are stated in ADR-0010 and here so they can be audited and tuned rather than discovered.
- Negative: memory per instance grows with active tenants. Entries are small and TTLs short; `scalability.md` §3 counts it alongside circuit memory.

## Revisit when

More than one web instance runs (introduce the Valkey L2), or a cache miss on any of the above shows up in a p95 latency profile, or the L1 working set becomes a material share of instance memory.
