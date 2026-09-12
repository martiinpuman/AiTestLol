# ADR-0019 — Object mapping

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Related:** ADR-0003 (EF Core), ADR-0006 (module contracts)

## Context

Aurora has three recurring mapping shapes: domain aggregate → contract DTO (constant), contract DTO → domain command (constant), and canonical document → country-specific format (ADR-0008 §7 point 4, which is genuinely bespoke and not a mapping-library problem). The first two are mechanical and voluminous; doing them by hand across a dozen modules is a lot of forgettable code, and doing them by reflection is a lot of silent runtime failure.

## Options considered

| Option | Licence (verified 2026-09-11) | Pros | Cons |
|---|---|---|---|
| AutoMapper | **Commercial** since 2025 | Ubiquitous, familiar | Fails `CLAUDE.md`'s permissive-licence rule. Independently: reflection-based, configuration discovered at startup, and a renamed property becomes a runtime null instead of a compile error — in a financial system that is a defect class we can simply not have |
| **Riok.Mapperly 4.3.1** *(chosen)* | **Apache-2.0** | A Roslyn **source generator**: the mapping is generated C# you can read and step into, with **no reflection and no startup configuration**; an unmapped or mistyped member is a **compile-time error or warning**, which is exactly the failure mode we want; zero runtime cost | Generated code must be understood by the team; complex projections still need hand-written LINQ |
| Mapster | MIT | Fast, flexible, compile-time option available | Its most common usage is runtime-configured, sharing AutoMapper's failure mode; smaller community |
| Hand-written mapping only | — | Explicit, no dependency, no magic | Hundreds of near-identical methods; the real risk is a forgotten field in a copy-paste, which review catches unreliably |

## Decision

**Mapperly is the sanctioned mapping tool, with three binding rules.**

1. **Never map *into* a domain aggregate or entity.** DTO → aggregate is forbidden. Aggregates are created and mutated only through their own constructors and methods, because that is where invariants live (ADR-0017 layer 2). Auto-mapping into an aggregate sets fields directly and destroys every guarantee the domain makes. A fitness test asserts no generated mapper targets a type in a `.Domain` assembly.
2. **Mapping is a boundary concern.** Mappers live in `.Application` (domain → contract DTO) and in `Aurora.Web` (DTO → view model). No mapper in `.Domain`, ever.
3. **Mapperly's strictness is turned up**: unmapped source and target members are configured as errors, not warnings. A silently dropped `Money` amount must fail the build. This is the entire reason for choosing a compile-time mapper, so leaving it at the default would be pointless.

**Where Mapperly is not used:**

- **Query projections** are hand-written `Select(x => new Dto { ... })` so EF Core translates them to SQL. Running a mapper after materialising an entity fetches columns nobody needs and defeats the projection — a common and expensive mistake.
- **Canonical document → country format** (ADR-0008 §7 point 4) is explicit, tested mapping code inside a Country Package. A legal document format is not a mechanical field copy.

## Consequences

- Positive: mapping errors are compile errors; no reflection, no startup scan, no cost at runtime; generated code is readable and debuggable.
- Positive: Apache-2.0, actively maintained, and independent of the 2025 commercialisation wave that removed AutoMapper.
- Negative: a source generator in the build. Generator failures can be cryptic; the mitigation is that generated output is inspectable and can be committed temporarily when debugging.
- Negative: developers arriving from AutoMapper will look for runtime configuration and profiles and not find them. Documented in the module README template.
- Negative: rule 1 means DTO → command remains partly hand-written. Accepted deliberately: that boundary is where validation and invariants meet, and it deserves explicit code.

## Revisit when

Mapperly's maintenance stalls, or mapping volume turns out to be small enough that hand-written mapping is simpler than the generator's mental overhead — in which case removing the dependency is a straightforward, mechanical change, because the generated code already shows exactly what the hand-written version would be.
