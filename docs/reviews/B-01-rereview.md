# Re-review — TASK B-01: Solution skeleton and build-wide settings

**Branch:** `task/B-01` (3 commits, tip `6307256`) · **Base:** `claude/multi-tenant-saas-erp-pv2nap` (`7411223`)
**Reviewer:** senior-reviewer (not the author) · **Date:** 2026-09-11
**Supersedes:** the CHANGES_REQUESTED verdict in `docs/reviews/B-01.md`

## Verdict: APPROVE

M-1 is fixed correctly and the fix is verified. m-2 and m-3 are done and introduced no side effects. No new findings above nit level.

---

## Quality gate result

`scripts/verify.sh` does not exist yet (task B-02), so the gate is the Release build plus the solution test run. Run in a detached scratch worktree at `6307256`, SDK `10.0.401` (matches `global.json`), then removed.

| Check | Command | Result |
|---|---|---|
| Locked restore | `dotnet restore Aurora.sln --locked-mode` | **pass**, exit 0 — all 9 projects, no lock-file change |
| **Release build (the AC)** | `dotnet build -c Release --nologo` | **pass — 0 Warning(s), 0 Error(s)** |
| **Solution test run (M-1)** | `dotnet test Aurora.sln -c Release --no-build` | **pass, exit 0** — no aborted run |
| Format (future stage 2) | `dotnet format --verify-no-changes --severity warn` | pass, exit 0 |
| Tracked-file mutation after restore + build | `git status --porcelain` | clean |
| Line-ending churn from `.gitattributes` | `git add --renormalize .` | **no output, nothing staged** |
| Fast-forwardable | `git merge-base --is-ancestor <integration> task/B-01` | **yes** — merge-base equals the integration tip |

The test output now enumerates exactly two assemblies (`Aurora.Architecture.Tests`, `Aurora.Web.ComponentTests`), both reporting "No test is available" and neither making the run non-zero. `Aurora.TestKit` is no longer handed to VSTest. This is precisely the behaviour the original review predicted and required.

### Rebase fidelity

`git diff 16dd60f bf898ff -- src tests Aurora.sln Directory.Build.props Directory.Packages.props .editorconfig global.json nuget.config scripts` is **empty** — the rebased first commit carries the reviewed tree byte-for-byte. The only delta between the old tip and the new base is docs the integration branch gained in parallel (`roadmap.md`, `SPEC-001`, `SPEC-002`, `B-01.md`, backlog/log/glossary). Nothing was silently dropped.

---

## Previous findings — status

**M-1 (major) — RESOLVED.** `tests/Aurora.TestKit/Aurora.TestKit.csproj:20` now carries `<IsTestProject>false</IsTestProject>` in the existing `PropertyGroup`, and `<PackageReference Include="xunit" />` at line 24 is retained as the original review required for B-10's collection fixtures. Verified end-to-end: build unchanged at 0 warnings, `dotnet test` exit 0. No other file in the repo keys off `IsTestProject`, so the property changes nothing else; `Directory.Build.props:60` sets `IsPackable=false` globally and is unaffected.

**The rewritten comment (lines 10–15) does its job.** It now names the mechanism (`IsTestProject=false`), states the failure mode in the words a developer would actually search for ("the test host fails to start because a library's output folder does not copy its NuGet dependencies"), and — the part that matters most — says "which aborts the whole solution test run, not just this project", which is the non-obvious consequence that made the original bug expensive. It also explains *why* the `xunit` reference is there, pre-empting the obvious wrong fix of deleting it. The false claim "`dotnet test` never targets it" is gone. This reads as a comment that would have saved the original reviewer an hour.

**m-2 (minor) — RESOLVED, cleanly.** `.gitattributes` adds `* text=auto eol=lf` and `*.sh text eol=lf`. `git add --renormalize .` reports nothing and stages nothing, so no tracked file changes representation — zero churn. No tracked file contains CRLF. `git check-attr text eol` confirms the rules resolve as intended on `.editorconfig`, `Aurora.sln`, `scripts/dev-env.sh` and the lock files. There are currently no binary tracked files, and `text=auto` uses git's own binary detection, so the broad `*` glob is safe as `docs/design/prototypes/` fills up.

**m-3 (minor) — RESOLVED.** The `<packageRestore>` block is gone from `nuget.config`. Everything load-bearing survives: `<clear />` on sources, the single pinned nuget.org entry, `<disabledPackageSources><clear /></disabledPackageSources>`, and the `packageSourceMapping` block with `<package pattern="*" />` scoped to `nuget.org`. Locked restore is green against this file, which exercises both the source and the mapping.

## New findings

### Blockers
None.

### Major
None.

### Minor
None.

### Nits (informational, no action)

**n-5 — `IsTestProject=false` will silently skip any test later added to `Aurora.TestKit`.** `tests/Aurora.TestKit/Aurora.TestKit.csproj:20`. This is the intended trade and the comment at line 11 states the contract ("it hosts no tests of its own"), which is the right guard. Recording it only so the pattern is on file: if B-10 ever wants to self-test a fixture, that test belongs in a consuming project, not here.

---

## Still open, deliberately out of scope for this branch

Both are architect-owned and confirmed still unaddressed on the branch. Neither was held against this verdict, per the brief.

- **m-1** — `Directory.Packages.props:81` and `:94` resolve `xunit.runner.visualstudio` to `3.1.4` and `Microsoft.CodeAnalysis.PublicApiAnalyzers` to `5.6.0`; `docs/architecture/dependencies.md` §3 still records them as "matching the framework major" / "latest stable". Architect writes the pins back with a 2026-09-11 verification date.
- **S-1 (major, against the spec)** — `dependencies.md` §1 rule 2 makes verify.sh stage 4 fail on the ~57 transitive packages already in the lock graph. Must be resolved before B-11 or the gate is red on day one.

---

## Pattern noted for this codebase

Build-file properties that change *which projects a tool targets* (`IsTestProject`, `IsPackable`, `TargetFramework` overrides) fail silently or fail far from their cause. When a `.csproj` comment asserts a tool's behaviour, the assertion and the property that produces it must sit on adjacent lines — the original M-1 was a broken run, but the expensive half was a comment that sent the reader somewhere else. Worth checking on every future build-file review: does any comment claim behaviour that no property in the file actually produces?
