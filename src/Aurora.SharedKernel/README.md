# Aurora.SharedKernel

Tier 0 (`docs/architecture/modules.md` §3). Every module depends on this assembly, so this assembly
depends on nothing but the BCL — no EF, no ASP.NET, no DI, no logging, no JSON — and no `double` or
`float` appears anywhere in it.

Both claims are enforced by `tests/Aurora.Architecture.Tests` (fitness rules **L1** and **F1**).
L1 reads this project's declared `PackageReference` elements and its committed
`packages.lock.json` as well as the emitted assembly references, so a package declared here and
never used still fails. F1 reads the IL of every method body, so a `double` local or a `(double)`
cast fails too — neither of which a rule over member signatures can see.

**Do not add a `PackageReference` or a `ProjectReference` to this project.**

Everything here is a type nobody can change cheaply once a ledger contains a million rows, so the
set is deliberately small and each member earns its place.

## What is here

| Type | What it is |
|---|---|
| `Currency` | An ISO 4217 code plus its minor-unit count. Validates shape, not membership |
| `Money` | A `decimal` amount plus its `Currency`. Arithmetic, ordering, summation, rounding, allocation |
| `RoundingPolicy` | Decimal places plus the midpoint rule. There is no way to round without naming one |
| `CurrencyMismatchException` | Raised when amounts in different currencies are combined or compared |
| `UnitOfMeasure` | A UN/ECE Recommendation 20 unit code |
| `Quantity` | A `decimal` amount plus its `UnitOfMeasure`. The same shape as `Money`, deliberately |
| `UnitOfMeasureMismatchException` | Raised when quantities in different units are combined or compared |
| `Percentage` | A rate, readable as a percentage or as a fraction, so the two cannot be confused |
| `DateRange` | A half-open run of calendar days |
| `IEntityId<TSelf>`, `EntityId` | The contract every strongly-typed identifier obeys, and its shared behaviour |
| `TenantId`, `CompanyId` | The two identifiers the whole system shares |
| `CompanyScope` | Which companies of the tenant a unit of work may see: every one, or a named set. The empty set cannot be built |
| `ICompanyScoped` | Marks an entity that belongs to one company, so that a `CompanyScope` can filter it. A company is scoped to itself |
| `Result`, `Result<TValue>`, `Error`, `ErrorKind` | How an expected failure is returned rather than thrown |

## The decisions worth knowing before you use these

**Money is a decimal amount and a currency, and nothing rounds implicitly.** Arithmetic keeps full
precision; rounding happens at the four points ADR-0021 §4 names and only where a caller passes a
`RoundingPolicy`. Amounts in different currencies throw rather than converting, because converting
needs a rate, a rate source and a date — all document data.

**`Money.Allocate` uses the largest-remainder method** (ADR-0021 §6). The parts add back up to the
total exactly; leftover minor units go to the largest fractional remainders, earliest part first on
a tie, so a split is repeatable. It refuses to split an amount that is not already whole minor
units, rather than rounding on the caller's behalf.

That the parts add up is guaranteed by how the split is computed, not by care taken while
computing it. `Allocate` scales the weights to whole numbers once and runs the whole
largest-remainder computation in `BigInteger`, so there is no operation in it that can round; it
then checks the split against its own law and throws rather than return parts that do not add up.
The earlier implementation multiplied the amount by each `decimal` weight, and **`decimal` rounds
silently once a result needs more than its 28–29 significant digits** — a weight written the
natural way, `lineAmount / documentTotal`, already carries 28 decimal places, so an ordinary
invoice lost a cent with no exception and no residual to explain it (review `docs/reviews/B-03.md`,
finding B-1). Any future money algorithm here that multiplies an amount by a caller-supplied factor
and then depends on the result being a whole number of minor units must do the same: work in
integers, or verify its own postcondition before returning.

**`Quantity` is `Money`'s twin.** Units do not mix, for the same reason currencies do not. There is
no unit conversion here: turning cartons into pieces needs an item's conversion factor.

**`DateRange` is half-open — `Start` included, `EndExclusive` not.** This is the type's whole
purpose: consecutive periods tile exactly, so no transaction on a period boundary is counted twice
or missed. `FromThrough` takes the inclusive last day for the way a period is written on a
document, and `LastDay` reads it back that way; both spellings build the same value. A range that
ends where it starts is empty, which is a state an inclusive range cannot express.

**Identifiers are UUIDv7 wrapped in their own type** (ADR-0007 §4). `TenantId` cannot be passed
where `CompanyId` is wanted. `IEntityId<TSelf>` carries both halves of an EF Core value conversion
so one converter serves every identifier; `EntityIdContract<TId>` in the test project holds each new
identifier to the same invariants.

**`CompanyScope` has two forms and no empty one** (ADR-0029 §5 and A1.2 H-2; ADR-0010 rules 4
and 6). `AllCompaniesInTenant` is the scope of a Role Assignment that names no company — every
company in the tenant, including ones created later — and emits no predicate, because tenant
isolation is the database. `Of(ids)` is a named set: it deduplicates and orders, so two scopes over
the same companies are one value with one hash code, and it throws on an empty collection and on
any unassigned (default) `CompanyId` rather than dropping it. The empty set is refused at
construction because an empty filter list that a query builder turns into no filter is
`WHERE 1=1`, which is every company — the company-level twin of a cross-tenant read. It is a sealed
class rather than a struct because a struct `default` would have to mean something, and neither
"every company" nor a third form is acceptable. A consumer branches on `IsAllCompaniesInTenant`,
never on the count of `CompanyIds`. How the scope reaches a query is decided but not yet built:
ADR-0029 A1.2 H-2 makes it a constructor parameter of any `DbContext` that maps an
`ICompanyScoped` entity, never an ambient lookup, and B-06.3 builds that rule — this assembly
holds the marker and the value, not the mechanism. A company implements `ICompanyScoped` by
returning its own id, so `Company` needs no special case.

**`Result` is for expected failures only.** A broken invariant still throws (ADR-0017 layer 2) and
input shape is still validated at the boundary (layer 1). `Result` exists so that "the period is
closed" is a value a caller handles, not a stack trace. It has no `Map`, `Bind` or `Match` on
purpose. A `default(Result)` is a failure, never a success.

## Time

There is no `IClock` here. `testing-strategy.md` §5 rule S3 and ADR-0020 settle on the BCL's
`TimeProvider`, injected, with `FakeTimeProvider` in tests — a wrapper interface would only stop
`FakeTimeProvider` from substituting. `modules.md` §3 still reads "`IClock` over `TimeProvider`";
that wording is on the architect's list to reconcile.

## Adding a type here

Ask first whether it belongs to one module instead. A type in the kernel is a type every module is
stuck with. If it does belong here: it takes a value object's shape (immutable, equal by value,
validating in its constructor), it throws rather than guessing when it is asked something it cannot
honestly answer, its `ToString` is culture-invariant and never localized, and every invariant it
claims has a unit test.
