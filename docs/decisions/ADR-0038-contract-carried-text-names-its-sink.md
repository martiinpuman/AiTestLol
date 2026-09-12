# ADR-0038 — Contract-carried text names its sink: package ids in SQL identifiers, routing violations in problem bodies

- **Status:** Accepted (2026-09-12)
- **Deciders:** architect
- **Supersedes:** —
- **Amends:** **ADR-0035 §2.2** — the chain it names is extended by one link and one of its links is shown to be unexecuted (§2.5). **ADR-0013 §3** — "a stack trace never crosses the boundary" is extended to an exception's `Message` and made fail-closed (§3.2).
- **Superseded by:** —
- **Related:** ADR-0007 §4.3, §9.2, ADR-0008 §3.1, §4.1, ADR-0013 §3, ADR-0016, ADR-0017, ADR-0018, ADR-0035 §2.2, `CLAUDE.md` (RFC 9457 for API errors; no personal data in logs)
- **Raised by:** the third security review of PR #13 (`task/B-06.1a`), two findings

> Both findings are one shape. A type in a `.Contracts` assembly carries text. The text is correct for the reader the type had in mind and dangerous for a sink the type never mentions. Neither the type nor the sink says so, and the sink is where the damage happens.

---

## 1. Context

Two findings from one review of `task/B-06.1a`:

1. **`InstalledPackageEntry.PackageId` is validated as "non-blank, no whitespace" while its own remarks call it *"the stem of the package's `pkg_<id>` schema (ADR-0008 §4.1)"*.** The type is honest about its scope — it says it *"refuses only what could not have come from that row at all"* — but `IsNullOrWhiteSpace` plus `Any(char.IsWhiteSpace)` reads to the next developer like a validated identifier, and it is not one. PostgreSQL's lexer treats `/**/` as a token separator, so this passes today:

   ```
   a;drop/**/schema/**/platform/**/cascade;--
   ```

   There is no live sink — nothing composes `pkg_<id>` yet — which is why the reviewer rated it low. But ADR-0008 §4.1 describes exactly that composition and B-13's installer will write it.

2. **`TenantRoutingViolationException.Mismatch`'s message names the other tenant's id and the database name.** Correct for an operator; wrong for an RFC 9457 problem body, which is client-facing. Nothing in the type says so, and B-06.2 wires this exception into the connection initializer.

`CLAUDE.md` requires RFC 9457 Problem Details for API errors and forbids personal data in logs. Finding 2 sits exactly on that boundary, and it is worth being precise about which side: a tenant id and a database name are **not personal data**, so logging them is correct and required (ADR-0016 wants the tenant on every log line). The hazard is **cross-tenant disclosure to a client** — telling tenant A's user the id of tenant B and the name of a database. That is a confidentiality question, not a privacy one, and the control for it is different.

---

## 2. Decision 1 — the identifier rule lives in both places, and the sink is the authority

### 2.1 The type gets the pattern, and the pattern is **exactly** the catalog's

`InstalledPackageEntry` validates the package id against the character rule the catalog enforces. The remarks clause the B-06.1a author proposed — one sentence saying this is not an identifier check — is **not** taken: making the type's guarantee match what every reader already assumes is better than a disclaimer the next reader has to find.

**But not the pattern the brief proposed.** `^[a-z][a-z0-9_]{0,30}$` is *stricter* than the catalog, and that direction is a bug, not extra safety:

> **`InstalledPackageEntry` is on the read path.** It materialises rows the catalog already accepted. A read-path type stricter than its writer means a legitimately stored row throws on the way into a `TenantScope` — and a `TenantScope` that cannot open is a tenant that cannot be served at all. A validation that turns bad data into an outage is worse than the data.

The pattern is therefore exactly what `ck_installed_package_id_well_formed` and `package_id varchar(32)` enforce today: **`^[a-z][a-z0-9_]*$`, at most 32 characters.** If the catalog's rule ever changes, the type's changes with it and in the same direction — which §2.2 makes automatic rather than remembered.

### 2.2 One source for the pattern, so there is nothing to keep in sync

Three places state this rule today and all three state it separately: `InstalledPackage.IsWellFormedPackageId` and `MaxPackageIdLength` in `Aurora.Platform.Tenancy`, the check constraint's SQL literal in `InstalledPackageConfiguration`, and (after §2.1) `InstalledPackageEntry`. Three copies of one rule need a mechanism to stay equal, and **the cheapest mechanism is not to have three copies.**

> A public `PackageIdFormat` in `Aurora.Platform.Tenancy.Contracts` holds `CharacterPattern` (`^[a-z][a-z0-9_]*$`), `MaxLength` (32), an `IsWellFormed(string)` and a `CheckConstraintSql(column)`. `InstalledPackageEntry` calls `IsWellFormed`. `InstalledPackage.Begin` calls `IsWellFormed`. `InstalledPackageConfiguration` builds **both** the check constraint and `HasMaxLength` from it.

`Aurora.Platform.Tenancy` already references `.Contracts`, so the direction is the one the layering allows. **This needs no synchronisation test, because a test that two copies are equal is itself a link somebody has to remember to write; one constant is a link the compiler follows.** It also touches no migration: the constraint SQL it generates is byte-identical to the literal already in `20260911172124_InitialCatalog`.

### 2.3 May a composer of `pkg_<id>` interpolate? **No. Ever.**

Asked explicitly, answered explicitly.

> **A composer of a `pkg_<id>` schema name may never interpolate a package id into SQL.** It must, in this order:
> 1. **Take a validated type, not a `string`.** Its parameter is an `InstalledPackageEntry` or a dedicated identifier value object — never a bare `string` that could have come from configuration, a manifest, a test fixture or a request.
> 2. **Re-validate against `PackageIdFormat` and refuse.** Not sanitise, not strip, not escape-and-continue: **refuse**, with the id in the exception message, because a value that fails here did not come from the catalog and the interesting fact is that it exists at all.
> 3. **Emit the identifier quoted**, through a quoting routine, never through string concatenation of the raw value.
> 4. **Be the only place in the solution that turns a package id into a schema name.** One function, and a fitness rule may later assert that the literal `"pkg_"` appears in exactly one production file.

**Why all four, when any one of them looks sufficient.** Each closes a different failure and none closes another's:

- Validation alone leaves a future widening of the pattern (a hyphen, a dot, a Unicode letter) silently re-opening injection. §2.6 shows that widening is not hypothetical.
- Quoting alone stops injection and **does not stop a wrong schema being created**: `quote_ident` of `a;drop schema x` is a perfectly safe identifier naming a schema nobody meant. Quoting protects the *database*; validation protects the *meaning*.
- Neither stops **silent truncation**. PostgreSQL truncates identifiers at 63 bytes, so `pkg_` plus a long enough id maps two distinct packages onto one schema — the worst outcome available, because it is a cross-package data merge that raises nothing. `MaxLength = 32` is what prevents it (`4 + 32 = 36`), which is why the length bound is part of the rule and not a detail of the column.
- The single-composer rule is what makes the other three auditable. Three composers means three places to check and two that will drift.

**Which link this stops at.** `NpgsqlCommandBuilder.QuoteIdentifier` exists in the pinned Npgsql 10.0.3 — I confirmed the symbol is present in the assembly. **I did not verify its escaping behaviour for adversarial input**, and this record does not assert it. The row that builds the composer must assert it: `"` doubled, the result round-tripping through `UnquoteIdentifier`, and the four adversarial inputs of §2.4. Naming the quoting routine is not the same as knowing what it does, and that distinction is the one this project keeps paying for.

### 2.4 What must demonstrate it

| | Assertion |
|---|---|
| **D1** | `InstalledPackageEntry` refuses `a;drop/**/schema/**/platform/**/cascade;--`, `ADMIN`, `1nz`, `nz-pack`, `nz.pack`, a 33-character id, and accepts `nz`, `nz_gst`, `a`, and a 32-character id. The rejected set is chosen so that each member fails for a *different* clause of the pattern |
| **D2** | `InstalledPackageEntry` accepts **every** value `ck_installed_package_id_well_formed` accepts. Expressed as: the same `PackageIdFormat.IsWellFormed` decides both, plus D3 |
| **D3** | **The check constraint refuses what the type refuses, in the database.** Insert each of D1's rejected ids into `catalog.installed_package` directly and require `SqlState == "23514"` with `ConstraintName == "ck_installed_package_id_well_formed"` — the shape `CatalogConstraintTests` already uses for eleven other constraints. Asserting the constraint *name* is what makes it a test of this rule rather than of whichever constraint fired first |
| **D4** | The composer refuses every D1 rejection, and its output for each accepted id is the expected quoted identifier. Plus the round-trip and escaping assertions of §2.3 |

### 2.5 The finding that matters more than the type: ADR-0035 §2.2's last link is unexecuted

ADR-0035 §2.2 states the chain and calls its terminus a mechanism rather than a convention:

> *"`InstalledPackageEntry` refuses only null, blank and whitespace → the sole writer is `catalog.installed_package` → `InstalledPackage.Begin` validates `^[a-z][a-z0-9_]*$` in the domain → `ck_installed_package_id_well_formed` enforces the same pattern in the database, for every principal including one writing raw SQL … **The link the entry type stops at is deliberate and the next link is a database check constraint, not a convention.**"*

**Two links of that chain are not what the sentence claims.**

- **The constraint has no test. None.** `ck_installed_package_id_well_formed` is declared in `20260911172124_InitialCatalog.cs:99` and referenced nowhere in `tests/`. `CatalogConstraintTests` asserts eleven constraints by name; this is not one of them. `CatalogSchemaTests` reads `pg_get_constraintdef` for `ck_tenant_state` and `contype` for `ex_subscription_no_overlap`, and has no inventory that would notice this one missing. **Nothing executed demonstrates that the database refuses a malformed package id** — not its effect, and not even its presence. The chain is intact in the migration source and unverified in execution, which is this project's recurring form *a check that verifies presence where only effect matters*, one step worse: here not even presence is verified. D3 is the fix.
- **"the sole writer is `catalog.installed_package`" is a statement about the world, not a mechanism.** `InstalledPackageEntry` has a `public` constructor in a `.Contracts` assembly that every module, host and job can reference. Any of them can construct one from any string. That is not an argument against the type — it is the argument *for* §2.1 putting the pattern in the type, which turns the sentence from a hope into an invariant of every instance.

### 2.6 A contradiction this record surfaces and deliberately does **not** decide

`catalog.installed_package.package_id` is `^[a-z][a-z0-9_]*$`, at most 32 characters. `Aurora.Countries.Contracts.PackageId` — the type `InstalledPackage`'s own remarks say *"can replace the CLR type without a schema change"* — is `^[a-z0-9]+(?:\.[a-z0-9]+)+$`, at most 128 characters, and its documented example is `aurora.country.nz`.

**`aurora.country.nz` cannot be stored in `catalog.installed_package.package_id`.** The dots are refused by the check constraint. Two merged pieces of work disagree about what a package is identified by, and ADR-0008 §4.1 is ambiguous in the same place — R1's prose says `pkg_<key>` with the example `pkg_nz`, and the sentence after it says packages own `pkg_<id>` schemas. They are two different identifiers: a **global id** (`aurora.country.nz` — the manifest, the on-disk directory, every log line) and a **schema key** (`nz` — the stem of `pkg_nz`).

**This record does not decide it**, and the reason is the one this project asks to be stated: resolving it changes a merged catalog table, and choosing between the three resolutions — store both, store the global id and derive the key, or keep the key and rename the column — depends on the installer's shape, and **B-13's installer does not exist**. A decision made now would be made against no requirement.

What is decided is that §2.3's composer takes **the schema key**, whatever the resolution calls it, and that §2.1's pattern is the schema key's. The contradiction is handed back as a question with a recommendation (§5).

---

## 3. Decision 2 — an exception message is operator-facing; the problem-details mapper is the boundary, and it is an allowlist

### 3.1 Not a remarks clause, not a second field, not two exception types

All three were considered (§4.2) and all three are per-type answers to a whole-codebase question. `TenantRoutingViolationException` is one of the exceptions a request can end on. Every other one has the same property — a message written for whoever has to fix it — and solving it once per type means solving it again for every type, forever, and missing one.

**The boundary is the mapper, and the mapper fails closed:**

> An exception reaches an RFC 9457 problem body **only** if its type is on an explicit map from exception type to `type` URI, `title`, status and a `detail` the mapper composes **from the exception's structured properties**, choosing which are safe to disclose. Every other exception produces a generic problem body — `500`, a stable `type`, a fixed `title`, no `detail` beyond "an unexpected error occurred" — and the `traceId` ADR-0013 §3 already requires. **An exception's `Message` is never copied into a problem body**, mapped or not, for the same reason ADR-0013 §3 already gives for a stack trace: it is written for a developer and it is not part of the API.

**`TenantRoutingViolationException` is then correct exactly as written.** Its message names the other tenant and the database, and that is what an operator asks first. Its `Expected`, `Found` and `DatabaseName` properties are what a structured log and an alert key on. And a client receives `500` with a `traceId` and nothing else, because a routing violation is never a client's problem to fix and *which other tenant* is never a client's business.

**This is the same shape as §2.3.** The value is fine; the sink decides what may be done with it; the sink fails closed so a value nobody thought about gets the safe treatment by default.

### 3.2 The cheap guard still goes on the type

A one-sentence remarks clause on `TenantRoutingViolationException` saying its message is **operator-facing and must not be rendered into a client response, per ADR-0038 §3** — as the cheap guard, with §3.1's mapper as the real one. Exactly the division §2.1/§2.3 draw: the type's clause costs a line and catches a reader; the mapper catches everything.

### 3.3 What must demonstrate it, and the link it stops at

- **D5** — a mapper test with an exception type deliberately absent from the map, asserting the body carries the generic `type`, no `detail` from the exception, and a `traceId`. The test must construct its exception with a message containing a recognisable secret-shaped string and assert that string appears nowhere in the serialised body. **Asserting the absence of the actual message text is what makes it fail if someone adds a `catch`-all that echoes `ex.Message`**; asserting only the status code would not.
- **D6** — a mapper test for `TenantRoutingViolationException` specifically, asserting the body names neither `Found` nor `DatabaseName`.
- **D7** — the mapper reports **how many exception types it has mapped**, and a test pins that count, so the map cannot silently empty.

**Where the chain stops: no problem-details mapper exists.** There is no `ProblemDetails` in `src/` or `tests/` anywhere in this repository, and no API endpoint to map for. **§3.1 is a specification and nothing enforces it**, in the present tense, until the row that builds the mapper lands. Until then the only thing standing between this exception's message and a client is that nothing renders a client response at all — which is a fact about the calendar, not a control.

---

## 4. Options considered

### 4.1 Where the package id rule lives

| Option | Pros | Cons |
|---|---|---|
| **A. In the type *and* at the sink, one shared pattern constant, sink authoritative** *(chosen)* | The type's guarantee matches what every reader already assumes; the sink is fail-closed against values that never went through the type; one constant means no drift and no sync test | Three call sites of one constant instead of three independent rules — cheap, but it is still a refactor of merged code |
| B. Remarks clause only, as the B-06.1a author proposed | Smallest possible change; honest about today's scope | It asks the next developer to read a disclaimer. The reviewer's finding is that the type *reads* like a validated identifier; a note saying "it isn't" leaves the misreading one skipped paragraph away |
| C. The pattern in the type, `^[a-z][a-z0-9_]{0,30}$` as the brief proposed | One line; visibly a real identifier check | Stricter than the writer by two characters. A 31- or 32-character id the catalog accepts would throw on the read path and take the tenant down. §2.1 |
| D. At the sink only | The sink is where it matters, and it must validate anyway | Leaves the type reading as a validated identifier, which is the finding; and a second composer written later has to rediscover the rule |
| E. A typed `PackageSchemaKey` value object in `.Contracts`, replacing the `string` | The strongest form — unrepresentable invalid states | It is §2.6's decision wearing a different hat, and §2.6 says why that is not decidable today. Worth revisiting the day B-13 exists |

### 4.2 Keeping an operator message out of a client response

| Option | Pros | Cons |
|---|---|---|
| **A. Allowlist mapper; `Message` never crosses; the type gets a remarks clause** *(chosen)* | Solves the class, not the instance; fails closed for exception types nobody has thought about; leaves the exception's message free to be as useful to an operator as it likes | Needs a mapper that does not exist; every genuinely client-facing error must be added to the map deliberately, which is work and is the point |
| B. A remarks clause on `TenantRoutingViolationException` alone | One line, today | One type out of the many a request can end on. The next one is unguarded, and the guard is a sentence a mapper author must have read |
| C. A separate operator-only field, keeping `Message` client-safe | Structured; no mapper needed | Inverts the default: `Message` becomes the *safe* thing, so every exception in the codebase must now be written defensively and any that is not leaks. It also makes `ToString()`, which is what a log and a crash dump print, the *less* useful rendering |
| D. Two exception surfaces — a public one and an internal one | Explicit at the type level | Two types per failure, a mapping between them, and a new way to be wrong: throwing the wrong one. And it still relies on a mapper doing the right thing |

---

## 5. Where each rule in this record stops

| Rule | Last link it follows | What is on the other side, unchecked |
|---|---|---|
| §2.1 the pattern in the type | `PackageIdFormat`, the one constant the type, the domain and the check constraint all read | Nothing, by construction — there is one string, so there is nothing to drift |
| §2.3 the composer refuses | `PackageIdFormat.IsWellFormed` at the sink, on a validated input type | **The quoting routine's behaviour.** `NpgsqlCommandBuilder.QuoteIdentifier` exists in the pinned Npgsql 10.0.3 — the symbol was confirmed present in the assembly — and **its escaping was not verified here**. D4 must assert it |
| §2.3 the single-composer rule | A fitness rule may assert `"pkg_"` appears in one production file | **Does not exist yet.** Until it does, "one composer" is a convention |
| §2.5 the catalog chain | `ck_installed_package_id_well_formed` | **Nothing executed it.** D3 is the test that closes this; the constraint is named in no test today |
| §3.1 the mapper allowlist | — | **Nothing at all.** No problem-details mapper exists in `src/` or `tests/`. §3.1 is a specification |

## 6. Consequences

**Positive**

- `InstalledPackageEntry` stops reading like a validated identifier by becoming one, and becoming exactly as strict as the row it materialises — no more, which is the part that is easy to get wrong in the dangerous direction.
- §2.3 answers "may a composer interpolate" with a rule that is four clauses rather than one, each closing something the others do not — notably identifier truncation, which neither validation-as-usually-written nor quoting catches.
- §2.5 turns an ADR's stated chain into an executed one, and finds that its last link had no test at all.
- §3.1 fixes a class instead of a type, and it fixes it in the direction where the exception that nobody thought about gets the safe treatment.

**Negative, and owned**

- **§2.6 is left open on purpose**, and something will hit it: B-13's installer is the first code that must store a package's identity and compose its schema name in the same breath. Leaving it open is the right call and it is not a free one.
- **§3.1 is a specification and nothing enforces it** (§3.3). This record creates a requirement on a component that does not exist, and says so in the present tense rather than describing it as though it did.
- **Three merged files change** for §2.2's shared constant. Small, but it is merged code being refactored for a rule that has no live sink yet.
- **The allowlist mapper makes every new client-facing error a deliberate act.** That is the intended cost and it will feel like friction the first few times.

---

## 7. Revisit when

- **B-13's installer is specified.** §2.6 must be decided before it writes a row, and §2.3's composer is its code. That is also when option 4.1 E — a typed schema key — becomes the obvious shape.
- **The package id pattern is widened for any reason** — a hyphen, a dot, a non-ASCII letter. §2.3's four clauses were written for that day; re-read them rather than assuming the composer is still safe.
- **The first API endpoint ships.** §3.1 stops being a specification then, and D5–D7 are its acceptance criteria.
- **A second composer of any SQL identifier appears** — a tenant database name, a role name, a partition name. §2.3 is the general rule and each of those deserves its own single composer, not a copy of this one.
