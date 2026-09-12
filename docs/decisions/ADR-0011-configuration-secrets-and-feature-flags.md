# ADR-0011 — Configuration, secrets and feature flags

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Related:** ADR-0007 (catalog), ADR-0012 (caching)

## Context

Three different things are routinely confused: **host configuration** (connection endpoints, log levels — same for every tenant), **tenant settings** (a tenant's default warehouse, invoice numbering, rounding policy — business data), and **feature flags** (a release toggle, usually temporary). Storing them in one place means either secrets end up in the database or business settings end up needing a redeploy.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Everything in `appsettings.json` + environment variables | Simplest | A tenant cannot change their own settings without a deployment. Unworkable for an ERP |
| Everything in the database | One place; runtime-changeable | Secrets in the database; bootstrap paradox (you need a connection string to read the connection string); log level changes need a database round trip |
| **Three tiers, each with its own store** *(chosen)* | Each concern in the store that fits it; secrets never touch the repository or the database | Three mechanisms to learn |
| A SaaS flag provider (LaunchDarkly and similar) | Excellent tooling, targeting, gradual rollout | Forbidden by the hard limits (no sign-ups, no spending); an external dependency on the request path |

## Decision

**Tier 1 — host configuration.** `appsettings.json` + `appsettings.{Environment}.json` + environment variables, via `IOptions<T>` with `ValidateDataAnnotations().ValidateOnStart()`. A misconfigured host fails at startup, loudly, rather than at 02:00 on the first request that needs the value. Strongly-typed options only; no `IConfiguration["some:key"]` scattered through the code (a fitness rule).

**Tier 2 — secrets.** From the host's secret store, injected as environment variables or mounted files. **Never** in the repository, never in `appsettings.json`, never in the catalog or a tenant database — the catalog stores a *reference* (`admin_secret_ref`, `app_secret_ref`), never a value (ADR-0007 §9.2). Locally, .NET user secrets. `verify.sh` includes a secret scan over the diff. Rotation is an overlap window, not a cutover.

**Tier 3 — tenant settings.** `platform.setting` in the **tenant** database, scoped to tenant or company, typed and validated, audited on change (ADR-0018), cached per tenant with explicit invalidation. Business settings are business data: they live with the tenant, they get backed up with the tenant, and they are exported on offboarding.

**Feature flags** — `Microsoft.FeatureManagement` 4.7.0 (**MIT**, verified 2026-09-11) with a custom `IFeatureDefinitionProvider` reading `catalog.feature_flag` plus `catalog.feature_flag_tenant_override`. Rules:

1. Every flag has an **owner and an expiry date** recorded at creation.
2. **A test fails the build when a flag is past its expiry.** Permanent feature flags are how a codebase acquires untested branches nobody dares delete; the expiry test is the only mechanism that reliably prevents it.
3. Flags gate *releases*, not *products*. A capability a customer pays for is a subscription entitlement in `catalog.subscription`, evaluated separately. Conflating the two means a billing change requires a code deployment.
4. A flag is never read in the domain layer.

## Consequences

- Positive: a tenant changes their own settings without us; we change log levels without touching tenant data; secrets are absent from every store we back up.
- Positive: `ValidateOnStart` turns a class of production incidents into failed deployments.
- Negative: three stores means a developer must know which tier a value belongs to. The test: *does it differ per tenant?* → tier 3. *Is it a credential?* → tier 2. *Otherwise* → tier 1.
- Negative: flag evaluation touches the catalog. Cached for 60 seconds with tenant-prefixed keys (ADR-0012); a catalog outage degrades to last-known values rather than an outage.

## Revisit when

Flag targeting needs percentage rollouts per tenant cohort (FeatureManagement supports filters — likely sufficient), or tenant settings grow past a few hundred keys and need a schema rather than a key/value table.
