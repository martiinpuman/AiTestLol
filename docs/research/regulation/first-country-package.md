# First Reference Country Package — Recommendation

Status: draft v1. Author: researcher. Date: 2026-09-10.

## Conclusion

**Build New Zealand as the first reference Country Package.** Australia is the second choice and a natural second package (they share the same open e-invoicing specification), not a rejection. Singapore and the UK are both good and well-documented but each carries one specific gate or complexity that NZ does not, and Ireland is rejected for this specific "first" purpose primarily on VAT-model complexity relative to NZ.

This recommendation weights **"clearly documented and ungated" far above "big market,"** exactly as instructed: the first package's job is to prove our Country Package extension points are real, not to win New Zealand as a market. New Zealand is not expected to be a meaningful revenue source for this product; it is expected to be the cheapest possible way to find out whether our extension-point design (see `features/localization-packages.md`) actually works end-to-end against a real jurisdiction's rules.

---

## Candidates compared

Criteria, each scored qualitatively (not numerically — the differences are more about kind than degree):
- **Doc quality:** primary-source, English, freely accessible documentation quality.
- **No paid gate:** absence of a paid certification/accreditation scheme required just to build correct software (not to become a network operator).
- **Open e-invoicing spec:** a publicly published, non-proprietary e-invoicing specification.
- **Open interchange:** an openly documented accounting-data interchange or audit-file format (few countries have one; explicitly weighed as "present/absent," not a required feature).
- **Representativeness:** how much of what we build transfers to other jurisdictions.

### New Zealand — recommended
- **GST:** flat 15% standard rate with very few reduced rates or exemptions compared to most VAT systems — the simplest possible correct implementation of the tax-rules extension point, which is exactly what a *first* package should exercise (prove the mechanism, not its most complex case).
  Source: https://en.wikipedia.org/wiki/Goods_and_Services_Tax_(New_Zealand) (cross-checked against https://www.getsphere.com/blog/new-zealand-gst-rate) — accessed 2026-09-10.
- **Filing:** all GST returns are filed electronically through Inland Revenue's **myIR** portal; a documented GST API and "Gateway Services" exist for software integration. Access requires only self-service registration to IRD's Gateway Customer Support Portal and use of a published sandbox — **no accreditation fee or tiered certification program is described anywhere in the sources found this pass**, in contrast to Singapore's ASR+/DII scheme below.
  Source: https://www.ird.govt.nz/digital-service-providers/services-catalogue/returns-and-information/goods-and-services-tax and https://www.ird.govt.nz/topics/intermediaries/gateway-services — accessed 2026-09-10.
- **Identifier validation:** the New Zealand Business Number (NZBN) is a 13-digit GS1 Global Location Number with a **publicly documented check-digit algorithm**, and the NZBN public register is queryable via a **free API with no authentication required** for basic lookups.
  Source: https://www.nzbn.govt.nz/using-the-nzbn/nzbn-services/api/ and https://www.nzbn.govt.nz/ — accessed 2026-09-10.
- **E-invoicing:** New Zealand's e-invoicing network is Peppol-based, governed by MBIE (Ministry of Business, Innovation and Employment) as the Peppol Authority, using the **PINT A-NZ** specification (an openly published Peppol BIS Billing 3.0 profile shared with Australia). Central government agencies have accepted Peppol e-invoices since March 2022; from 1 January 2026 agencies handling 2,000+ domestic invoices/year must send and receive them, with a broader business mandate (NZD 33M+ revenue) from 1 January 2027 — i.e., the specification is already open and stable well ahead of the mandate, so we are not building against a moving target.
  Source: https://blog.eezi.io/the-future-of-e-invoicing-in-new-zealand-peppol-pint-a-nz-and-the-2026-electronic-invoicing-mandate/ and https://goroute.ai/blog/new-zealand-government-einvoicing-rules/ — accessed 2026-09-10.
- **Interchange format:** No NZ-specific mandatory accounting-interchange/audit-file standard was found this pass (UNVERIFIED as a negative — absence of evidence, not confirmed absence). This is actually informative: it means our reference implementation is free to adopt a genuinely international open standard (e.g., OECD's SAF-T schema, itself openly published) as NZ's "accounting interchange" contribution, proving the extension point without inventing a proprietary format or waiting on a country-specific one.
- **Representativeness:** Common-law, English-language, GST/VAT-style consumption tax, Peppol e-invoicing, GS1-based business identifier — all patterns that recur across dozens of other jurisdictions (Australia, UK, Singapore, much of the Commonwealth, and Peppol-network EU countries), so building this package genuinely exercises transferable machinery rather than a one-off.

### Australia — strong second choice
- Shares essentially the same Peppol/PINT A-NZ e-invoicing specification as New Zealand and the same open, checksum-validated business-identifier pattern (ABN, published algorithm, free ABN Lookup web service via the Australian Business Register).
  Source: https://goroute.ai/australia.html and https://goroute.ai/blog/peppol-e-invoicing-australia-guide/ — accessed 2026-09-10.
- GST is also comparatively simple (10%, few exemptions relative to many VAT systems), and the ATO is the named Peppol Authority with published accreditation info for Access Points (again, not a gate on the software vendor who is not becoming an Access Point).
- **Why second, not first:** tax-office *lodgment* integration in Australia (Standard Business Reporting / digital-identity-based authentication) is reported to involve more setup friction (digital certificates/myGovID-based authentication for direct lodgment) than New Zealand's simpler Gateway Services self-registration — this is a difference in degree, not kind, and is flagged as **UNVERIFIED in detail** since this pass did not fetch ATO's own SBR onboarding documentation directly. Recommend Australia as the natural **second** package specifically because so much (the e-invoicing profile, the identifier-validator pattern) is directly reusable from the New Zealand package — a good test of whether our extension points compose cleanly across two similar-but-distinct jurisdictions.

### Singapore — good documentation, heavier accreditation ladder
- IRAS's own English-language documentation is excellent (the IRAS API Developer Portal, the Singapore Peppol Guide) and GST is simple (flat 9% since 2024, per prior general knowledge — not independently re-verified this pass, flag as UNVERIFIED).
- However, Singapore has built a **visible, tiered software-accreditation program**: the "Accounting Software Register Plus" (ASR+) with named tiers (e.g. "Tier 3" requiring GST return, corporate income tax return, and InvoiceNow capabilities all integrated), plus a **Digital Integration Incentive (DII)** — a government incentive scheme to accelerate vendor integration. This is not necessarily a *paid* gate, but it is a materially more structured accreditation ladder than New Zealand's plain self-service registration, and is exactly the kind of gate the brief asks us to weight against.
  Source: https://www.iras.gov.sg/who-we-are/what-we-do/annual-reports-and-publications/taxbytes-iras/engagement/three-software-developers-attain-asr-tier-3-status and https://www.iras.gov.sg/digital-collaboration/for-software-developers/accounting-tax-software/iras-digital-integration-incentive-(dii) — accessed 2026-09-10.
- **Why not first:** the accreditation-tier structure is more than we need to prove the extension points work; better suited as a later package once we specifically want to test how our contract handles a jurisdiction with a formal vendor-certification program.

### United Kingdom — excellent docs, one real gate
- HMRC's Developer Hub is exceptionally well documented (100+ published APIs, explicit end-to-end service guides for VAT and Income Tax Making Tax Digital).
  Source: https://github.com/api-evangelist/hmrc and https://developer.service.hmrc.gov.uk/api-documentation/docs/api — accessed 2026-09-10.
- **Why not first:** software must go through an explicit **HMRC recognition process** before it can interact with production MTD APIs — developers must complete a defined test process, then contact HMRC's SDS team and wait ~10 working days for a decision, plus complete fraud-prevention-header conformance questionnaires. This is free but is unambiguously a **formal approval gate**, which the brief specifically asks us to weight against, and UK VAT itself is materially more complex than NZ/Australia GST (multiple rates, partial exemption, flat rate scheme) — appropriate for a *second or third* package once the extension points are proven, not the first.
  Source: https://developer.service.hmrc.gov.uk/guides/vat-mtd-end-to-end-service-guide/ — accessed 2026-09-10.

### Ireland — rejected for "first," good future candidate
- Excellent English-language Revenue documentation and clean access to the EU's VIES VAT-number validation service (a genuinely open, free, cross-border identifier-validation API — a good precedent for extension point #7 in `features/localization-packages.md`).
  Source: https://ec.europa.eu/taxation_customs/vies/ — accessed 2026-09-10.
- **Why not first:** Ireland's VAT model is the full EU multi-rate VAT system (standard/reduced/zero rates, intra-EU reverse-charge rules, EU Sales Lists) — materially more complex than NZ/Australia's near-flat GST, and Ireland does not yet have a domestic B2B e-invoicing mandate (only B2G via Peppol), so the e-invoicing extension point would be less fully exercised than in NZ/Australia today. Good second-or-third-package candidate specifically *because* it's more complex — once the mechanism is proven simple, prove it against a harder case.

---

## Recommendation for sequencing beyond the first package
1. **New Zealand** (first) — proves the mechanism cheaply against the simplest credible real jurisdiction.
2. **Australia** (second) — proves the mechanism composes across two jurisdictions sharing an e-invoicing spec but with distinct tax administrations.
3. **Ireland or the UK** (third) — proves the mechanism against real VAT-rate complexity (multiple rates, reverse charge) and, for the UK, a real formal software-recognition gate.

## Sources
1. https://en.wikipedia.org/wiki/Goods_and_Services_Tax_(New_Zealand) — accessed 2026-09-10
2. https://www.ird.govt.nz/digital-service-providers/services-catalogue/returns-and-information/goods-and-services-tax — accessed 2026-09-10
3. https://www.ird.govt.nz/topics/intermediaries/gateway-services — accessed 2026-09-10
4. https://www.nzbn.govt.nz/using-the-nzbn/nzbn-services/api/ — accessed 2026-09-10
5. https://blog.eezi.io/the-future-of-e-invoicing-in-new-zealand-peppol-pint-a-nz-and-the-2026-electronic-invoicing-mandate/ — accessed 2026-09-10
6. https://goroute.ai/blog/new-zealand-government-einvoicing-rules/ — accessed 2026-09-10
7. https://goroute.ai/australia.html — accessed 2026-09-10
8. https://www.iras.gov.sg/digital-collaboration/for-software-developers/accounting-tax-software/iras-digital-integration-incentive-(dii) — accessed 2026-09-10
9. https://developer.service.hmrc.gov.uk/guides/vat-mtd-end-to-end-service-guide/ — accessed 2026-09-10
10. https://ec.europa.eu/taxation_customs/vies/ — accessed 2026-09-10

## What would change my mind
- Direct confirmation (not obtained this pass — ATO's own SBR onboarding docs were not fetched) of exactly how much friction Australia's Standard Business Reporting lodgment integration involves; if it turns out to be as simple as NZ's Gateway Services, Australia and New Zealand would be roughly tied and market size might reasonably break the tie toward Australia.
- Confirmation of whether Singapore's GST rate and ASR+ requirements have changed since this pass (marked UNVERIFIED above).
- Whether NZ's Inland Revenue Gateway Services registration, on closer inspection once we actually attempt it, turns out to have hidden friction (e.g., a legal-entity or in-country presence requirement) not visible in the documentation surveyed — this is the single biggest risk to the recommendation and should be checked empirically (registering for the sandbox) before the architect commits engineering time to building against it.
