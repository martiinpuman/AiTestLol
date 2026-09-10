# Human inbox

Questions from the team, and the human's answers. **The human's answers override every earlier decision.**
Write answers directly underneath a question. The orchestrator turns them into tasks or ADR updates on the next iteration.

Newest entries at the top.

---

## 2026-09-10 — Bootstrap decisions (ANSWERED)

**Q1. Market and compliance scope?**
> **Answer:** Keep it generic. For country-specific features or laws, build **Country Packages** that a tenant can add to their system.

**Q2. Database?**
> **Answer:** PostgreSQL.

**Q3. Blazor hosting model?**
> **Answer:** Blazor Server / `InteractiveServer`.

**Q4. Tenant isolation model?**
> **Answer:** **Database per tenant.**
> *(Noted by the orchestrator: the recommendation had been shared-schema + row-level security. The human chose the stronger-isolation option. Consequences — per-tenant migration orchestration, connection pooling limits, and provisioning cost — are the architect's to design for, not to relitigate. See ADR on multi-tenancy.)*

---
