---
name: analyzer-hardening
description: Harden one LinqContraband rule end to end (pick it from docs/analyzer-health.md, TDD the fix, update docs, health doc and changelog, open a PR). Use for any analyzer or code-fix improvement in this repo.
---

# Analyzer hardening loop

One rule per iteration, unless `docs/analyzer-health.md` names a tightly coupled pair (for example LC018/LC034/LC037). Read `.claude/CLAUDE.md` first for build commands and load-bearing facts.

## 1. Pick the target

- If the user named a rule or bug, work on that.
- Otherwise read `docs/analyzer-health.md`: the **Planning Shortlist**, then the **Scorecard** Priority column, then the **Importance Ranking**. Pick the rule that is highest in importance *and* has a named gap. Skip items listed as rejected or deferred by design.
- A bug report is a request for the fix: reproduce it as a failing test first.

## 2. Understand the rule

- Rule metadata: `src/LinqContraband/Catalog/RuleCatalog*.cs` (domain folder, severity, fixer or no-fix rationale).
- Source: `src/LinqContraband/Analyzers/<Domain>/LCxxx_Name/`. Large analyzers are split into partial files by concern; follow the existing split.
- Tests: `tests/LinqContraband.Tests/Analyzers/LCxxx_Name/`. Reuse the file's `Preamble`/mock constants rather than writing new EF mocks.
- Docs: `docs/LCxxx_Name.md`. Sample: `samples/LinqContraband.Sample/Samples/LCxxx_Name/` plus `samples/LinqContraband.Sample/sample-diagnostics.json`.
- Shared helpers live in `src/LinqContraband/Extensions/`. Prefer them over new ad-hoc symbol checks.

## 3. TDD

1. Write the failing tests first: at least one "crime" case that must report (mark spans with `{|LC0xx:...|}`) and the nearest "innocent" shapes that must stay quiet.
2. Run only that rule's tests: `dotnet test --no-build -f net10.0 --filter "FullyQualifiedName~LC0xx"` (after `dotnet build`). Confirm the new tests fail for the expected reason.
3. Implement the smallest analyzer or fixer change that makes them pass. Stay conservative: when the analyzer cannot prove an EF-backed shape, it stays quiet. A false positive is worse than a missed report.
4. If the change makes a new shape report and the rule has a fixer, add that shape to the rule's fixer-coverage corpus (for LC045, `MissingIncludeFixerCoverageContractTests`) and prove the fix compiles.
5. Fixers must never produce uncompilable or behavior-changing code. If a shape has no safe rewrite, report it without a fix and add a test proving no fix is offered.

## 4. Update the surfaces

- `docs/LCxxx_Name.md`: new reporting shapes, new safe cases, new non-goals.
- Sample and `sample-diagnostics.json` if the sample's diagnostics change.
- `docs/analyzer-health.md`: update the rule's Scorecard row (scores only move with evidence, per the rubric's harsh calibration), the test count in the note, the "Reviewed:" line and suite total at the top, and the Planning Shortlist if the lead is closed. Add a dated `## YYYY-MM-DD LCxxx <topic> pass` section for anything non-trivial.
- `CHANGELOG.md`: an entry under `## [Unreleased]` (create the heading if missing) in the existing voice: what now reports or stays quiet, and why.
- `docs/rule-catalog.md` only via `RuleCatalogDocGenerator -- --write` if catalog metadata changed.

## 5. Verify before pushing

Run the full CI set from `.claude/CLAUDE.md`: build, full `-f net10.0` test suite, rule-catalog `--check`, and the sample diagnostics verifier. Re-read the diff adversarially for false-positive risk and Roslyn 4.3.0 API use (anything newer will not compile in the analyzer project).

## 6. Ship

- Branch per iteration, conventional commit (`fix(LC0xx): ...` or `feat(LC0xx): ...`), PR with Before/After, drive CI green, squash-merge.
- Package releases are separate `chore: release X.Y.Z` PRs. Follow the "Releasing" section of `CONTRIBUTING.md`.
