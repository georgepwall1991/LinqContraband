---
name: verify-rule
description: Verify a single LCxxx rule's five-surface contract and run just that rule's tests — a fast inner loop while iterating on one analyzer, before running the full suite. Checks analyzer/fixer source, tests, sample, doc, and catalog entry are all present and consistent, then runs the rule's filtered tests. Use when working on one specific rule (e.g. "verify LC014").
disable-model-invocation: true
---

# Verify One Rule

A focused inner-loop check for a **single** rule while iterating on it. The full
`RuleCatalogIntegrityTests` validates all 44 rules at once; this narrows to the one you're editing
so you get fast feedback before running the whole suite or `/pre-pr`.

## Input

- **Rule id** — `LCxxx` (required; ask if not given). Optionally the slug `LCxxx_Name`.

## What to check (the five surfaces — same rules `RuleCatalogIntegrityTests` enforces)

Resolve the rule's `slug`, `AnalyzerSourcePath`, `AnalyzerTypeName`, `FixerTypeName`, `HasCodeFix`,
and `NoCodeFixRationale` from `src/LinqContraband/Catalog/RuleCatalog.cs`, then confirm:

1. **Analyzer source** — `AnalyzerSourcePath` directory exists and contains **exactly one**
   `*Analyzer.cs` whose type name equals the catalog's `AnalyzerTypeName`.
2. **Fixer consistency** —
   - if `HasCodeFix` is true: exactly one `*Fixer.cs` matching `FixerTypeName`, and
     `NoCodeFixRationale` is null/blank;
   - if false: **no** `*Fixer.cs` present, and `NoCodeFixRationale` is a non-blank string.
3. **Tests** — `tests/LinqContraband.Tests/Analyzers/<slug>/` exists and has ≥1 `.cs` file.
4. **Sample** — `samples/LinqContraband.Sample/Samples/<slug>/` exists and the catalog's
   `SamplePath` file exists under it.
5. **Doc** — the catalog's `DocumentationPath` (`docs/<slug>.md`) exists.
6. **Catalog ↔ doc sync** — confirm the generated doc is current:
   ```bash
   dotnet run --project tools/RuleCatalogDocGenerator/RuleCatalogDocGenerator.csproj -- --check
   ```

Use the repo's search tooling (`find-definition`, `code-search`) or Glob/Read — do not hand-roll
raw `find`/`grep`.

## Then run just this rule's tests

```bash
dotnet test --filter "FullyQualifiedName~LC0XX"
```

If a fixer exists, confirm its `*FixerTests.cs` ran (TestCode/FixedCode round-trip).

## Optional adversarial pass

For a behaviour change, suggest invoking the **`analyzer-fp-fn-hunter`** agent on this rule to probe
for false positives/negatives before the full gate — that is the dominant bug class in this repo.

## Finish

Report a per-surface PASS/FAIL table for the rule, the filtered test result, and whether the catalog
`--check` is clean. If any surface is missing/mismatched, name the exact file/line to fix. Point the
user at `/pre-pr` for the full gate before pushing. Do not commit unless asked.
