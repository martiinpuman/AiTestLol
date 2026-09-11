# ADR-0025 — Hosting and packaging model

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Related:** ADR-0005 (Blazor Server), ADR-0007 (tenancy), ADR-0022 (ICU), `../architecture/scalability.md`

## Context

`CLAUDE.md` fixes the assumption: **containers, cloud-agnostic**, and the team never creates real cloud resources. The runtime must serve stateful Blazor Server circuits (ADR-0005), reach thousands of tenant databases (ADR-0007), run scheduled and queued work (ADR-0014), and be upgradable without downtime for tenants whose schemas are mid-migration (ADR-0007 §7.5).

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **OCI containers, orchestrator-agnostic; `docker compose` locally** *(chosen)* | Runs identically on a laptop, in CI and on any orchestrator; no vendor API in the codebase; the team can exercise the whole system without creating a single cloud resource | We must specify the deployment constraints (sticky routing, draining, secret injection) that a PaaS would have implied |
| A specific PaaS (Azure App Service / Container Apps, AWS App Runner) | Least operational work; managed TLS, scaling, secrets | Vendor lock-in in configuration and deployment; requires a sign-up and spending — hard limits; and the "cloud-agnostic" assumption is a product parameter, not a preference |
| Virtual machines with systemd | Simple, old, well understood | No standard packaging, drifting hosts, a bespoke deployment pipeline, and a local development story that does not match production |
| Serverless functions | Elastic, pay-per-use | Incompatible with a stateful SignalR circuit, and per-tenant connection pools would be re-created on every cold start — pathological under ADR-0007 §5 |

## Decision

**One OCI image, two entrypoints. Cloud-agnostic. Nothing in the codebase names a cloud provider.**

### 1. The image

- Base `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (or the equivalent ICU-bearing chiselled image). **The image must contain ICU**: `InvariantGlobalization` is `false` (ADR-0022 rule 5), so an ICU-less base is not an option however small it is.
- Runs as a **non-root** user, read-only root filesystem, no shell where the base allows it.
- Multi-stage build; the SDK never ships in the runtime image.
- Tagged with the git SHA and the semantic release version; the **core contract version** (ADR-0008 §3.1) is an image label so a package-compatibility check can read it without starting the app.
- **`Aurora.Web` and `Aurora.Worker` are the same image with different entrypoints** (`overview.md` §3), so application code and schema expectations can never drift between them. This is not a packaging convenience; it is a correctness property.

### 2. Deployment constraints the orchestrator must satisfy

These are requirements, not preferences, and they are written here because a deployment that ignores them will fail in ways that look like application bugs.

1. **Sticky routing (session affinity) for `Aurora.Web`.** An active Blazor circuit cannot move between instances (ADR-0005). .NET 10's persisted circuit state lets a *reconnect* land elsewhere; it does not remove the need for affinity while connected.
2. **WebSocket support and generous idle timeouts** on the load balancer. A proxy that closes an idle WebSocket after 60 seconds turns every coffee break into a reconnect banner.
3. **Graceful shutdown with draining.** On `SIGTERM`: stop accepting new circuits, let existing ones finish or reconnect elsewhere, finish in-flight jobs or return them to `Ready`, then exit. `ShutdownTimeout` is set well above the longest expected request.
4. **Rolling deployment.** Safe because migrations are expand/contract and code version N runs against schema N−1 and N (ADR-0007 §7.2, §7.5). Blue/green is not required and is expensive at this database count.
5. **Secrets injected by the orchestrator** as environment variables or mounted files (ADR-0011). Never baked into the image, never in the repository.
6. **Health probes:** liveness = process; readiness = catalog reachable plus this instance's warm tenants (ADR-0016 §6). **A probe must never fan out across tenant databases.**
7. **Workers scale independently of web instances**, because their load is fleet-shaped (migrations, provisioning, outbox) rather than user-shaped.
8. **PgBouncer** is deployed alongside each database cluster from the 1 000-tenant stage (`scalability.md`); the migration runner deliberately bypasses it (ADR-0007 §7.3).

### 3. Local development

`docker compose` brings up PostgreSQL 17, the web and the worker. `scripts/dev-env.sh` puts the SDK on PATH and starts Docker. This is the *entire* runtime story this team operates: no cloud resources, no deployments, no spending (`CLAUDE.md` hard limits).

### 4. What is deliberately not decided here

Orchestrator choice (Kubernetes, Nomad, ECS, a single Docker host), TLS termination, backup storage and the monitoring backend are **operational decisions for whoever runs this**, constrained by §2 and by ADR-0007 §11. Choosing them now would embed assumptions we cannot test and would violate the cloud-agnostic parameter.

## Consequences

- Positive: the same artefact runs on a laptop, in CI and in production; no provider SDK appears anywhere in the codebase.
- Positive: the two-entrypoint image makes web/worker version skew impossible — a real class of bug in split deployments.
- Positive: the deployment constraints are written down before anyone deploys, so a failure to meet them is a deployment defect rather than a mystery.
- Negative: we get none of a PaaS's managed conveniences; someone must operate PostgreSQL, PgBouncer, TLS, backups and the orchestrator.
- Negative: sticky routing constrains load balancing and makes instance loss more disruptive than it would be for a stateless API. That is Blazor Server's cost, recorded in ADR-0005 and priced again here.
- Negative: a chiselled ICU-bearing image is larger than the minimal one. Non-negotiable — see ADR-0022.

## Revisit when

The product owner chooses a hosting provider (add a provider-specific deployment ADR that must not leak into the codebase), scale requires per-region deployments (`scalability.md` and ADR-0007 §11.3 already anticipate this), or .NET changes its container base image story.
