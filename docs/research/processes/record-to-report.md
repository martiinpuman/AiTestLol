# Record-to-Report (R2R) — Accounting Core and Period Close

Status: draft v1. Author: researcher. Date: 2026-09-10.

## Conclusion

Record-to-report has two distinct rhythms that our design must support simultaneously: **continuous** posting from every other process (O2C, P2P, inventory) into an immutable ledger, and a **periodic** close cycle (typically monthly) that reconciles, adjusts, revalues, and finally locks a period. The single most consequential design decision: **every module posts to the ledger through one core posting service that enforces immutability and produces only balanced, reversible entries** — record-to-report is not "the accounting module," it is the one place every other module's financial effect converges, which is exactly why CLAUDE.md makes posted-entry immutability and reversal-only correction a hard rule rather than an accounting-module concern. Multi-currency revaluation and period close are the two mechanics most incumbents differentiate on (Sage Intacct's and NetSuite's own marketing centers on close automation), so this is also a good area for us to aim to do well, not just adequately.

---

## The core flow

```
Every module (O2C, P2P, inventory, payroll-if-any)
        |
        v
   Journal Entry (always balanced, always tagged: who/when/what/tenant)
        |
        v
   General Ledger (posted = immutable)
        |
        v
   [Period-end only] --> Reconciliation --> Accruals/adjustments --> FX revaluation --> Trial balance review --> Period lock --> Financial statements
```

### 1. Continuous posting
- **Documents:** Journal entries, each originating from a business event elsewhere (invoice posted, payment received, goods received, inventory valuation change) or entered directly (manual journal, with appropriate authorization).
- **Rule (CLAUDE.md, corroborated by every incumbent's own design):** posted entries are immutable; a correction is always a new reversing entry, never an edit.
- **Roles:** Every module posts through the accounting core; accountants only manually journal for adjustments/accruals that don't originate from another module's event.

### 2. Reconciliation (ongoing, intensifies at period end)
- **What it is:** Matching internal sub-ledgers (AR, AP, inventory) and external sources (bank statements) against the general ledger control accounts, and investigating discrepancies before the period closes.
  Source: https://www.numeric.io/blog/month-end-reconciliation — accessed 2026-09-10.

### 3. Accruals and adjustments
- **What it is:** Recognizing revenue/expense in the period it belongs to even if cash or an invoice hasn't moved yet (e.g., goods received but not yet invoiced by the vendor; a service delivered but not yet billed).
- **Standard sub-process:** pull the prior period's accrual schedule → verify prior accruals reversed correctly → identify new period-end accruals needed → post them → confirm the balance sheet ties to supporting detail.
  Source: https://www.finoptimal.com/resources/how-to-reconcile-accruals — accessed 2026-09-10.
- **Decision point:** Accruals, provisions and estimates require an explicit documented basis and should be compared against prior periods and operational evidence — i.e., this is a control point, not a mechanical calculation, and our design should support an approval/evidence trail on manual accrual entries.

### 4. Foreign-currency revaluation
- **What it is:** At period end, any open balance denominated in a foreign currency (open AR, open AP, foreign-currency bank/cash balances) is *revalued* at the period-end spot rate. The resulting difference from the rate originally posted is booked as an **unrealized** gain/loss (a "paper" adjustment on a position still held) — distinct from a **realized** gain/loss, which is booked only when the foreign-currency amount is actually settled/converted.
  Source: https://softledger.com/blog/foreign-currency-revaluation-definition-process-and-examples and NetSuite's own documentation on foreign currency revaluation (https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N1409370.html) — both accessed 2026-09-10.
- **Design implication:** this requires the ledger to distinguish, per open item, its original transaction-date rate from the current revaluation rate, and to post the unrealized gain/loss to a *different* GL account than a realized one — two accounts, not one, per currency pair, is the standard pattern. Since CLAUDE.md treats "no jurisdiction privileged" as implying routine cross-border trade, this mechanic should be built as a first-class period-close step, not a later addition (also flagged in `01-feature-priority.md`'s multi-currency row).

### 5. Trial balance review and period lock
- **What it is:** Final review of the full trial balance before the period is locked against further posting (or only reversal/adjustment postings are allowed into a locked period, per policy).
- **Decision point:** What is allowed to post into a locked period — nothing, or only specially-authorized adjusting entries? This should be a configurable control, not assumed.

### 6. Financial statement production and statutory reporting
- Depends on the period being closed; this is where a Country Package's statutory-report-definition extension point (see `features/localization-packages.md`) is exercised.

---

## Nasty edge cases

- **Multi-currency revaluation timing vs. realized settlement**, as above — a single open invoice can be revalued (unrealized) at two or three successive month-ends before finally being paid and realized; the ledger must carry the running unrealized adjustment without disturbing the original transaction amount.
- **Reversals crossing a period boundary:** if an error is discovered after a period has been locked, the correction must post into the *current open* period as a reversal referencing the original entry, never by reopening the locked period — this is both an accounting-control requirement and consistent with CLAUDE.md's audit-log/immutability rule.
- **Rounding at the ledger level:** aggregated rounding differences from many small invoice/tax roundings (see `order-to-cash.md`) need a dedicated small "rounding" account so they don't silently distort a real account balance.
- **Intercompany and multi-entity consolidation** (relevant once a tenant has more than one legal entity, e.g. after installing a second Country Package for a new country of operation, per the localization doc's finding that no incumbent cleanly supports this today): intercompany transactions must reconcile between entities before consolidation — flagged as a genuine open design question for the architect, not solved by this research pass.
- **Accrual reversal discipline:** a forgotten reversal of a prior accrual is one of the most common real-world close errors per the sources above — worth building an automatic "reverse on the 1st of next period" option into the accrual entry itself rather than relying on manual follow-through.

---

## Roles summary
| Role | Responsibility |
|---|---|
| Accountant / bookkeeper | Reviews postings, enters manual journals/accruals, runs reconciliations |
| Controller / finance lead | Approves period lock, reviews trial balance, owns close checklist |
| Every other module (system role) | Posts balanced journal entries as a side effect of its own transactions — never writes the ledger directly |

## Sources
1. https://www.numeric.io/blog/month-end-reconciliation — accessed 2026-09-10
2. https://www.finoptimal.com/resources/how-to-reconcile-accruals — accessed 2026-09-10
3. https://ramp.com/blog/month-end-close-process — accessed 2026-09-10
4. https://softledger.com/blog/foreign-currency-revaluation-definition-process-and-examples — accessed 2026-09-10
5. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N1409370.html — accessed 2026-09-10
6. https://learn.microsoft.com/en-us/dynamics365/finance/general-ledger/foreign-currency-revaluation-general-ledger — accessed 2026-09-10

## What would change this
UNVERIFIED this pass: how competitors actually handle a correction discovered *after* an external statutory filing has already been submitted against a locked period (e.g., a VAT return already filed) — this is a materially harder edge case than an ordinary internal reversal and deserves its own targeted regulation-focused research pass once we pick the first Country Package's specific filing mechanics (see `regulation/first-country-package.md`).
