# LinqContraband — Project Guide for Claude Code

**LinqContraband** is a Roslyn analyzer (NuGet package, id `LinqContraband`) that catches EF Core
query anti-patterns at compile time. 45 rules (`LC001`–`LC045`) across 8 "neighborhoods", each
shipped with an analyzer, tests, a sample, a doc page, and a central catalog entry.

- Analyzer project targets **`netstandard2.0`**; tests/samples multi-target **net8.0/9.0/10.0**.
- Roslyn pinned to **Microsoft.CodeAnalysis.CSharp 4.3.0**. Tests use **xUnit** +
  `Microsoft.CodeAnalysis.CSharp.{Analyzer,CodeFix}.Testing.XUnit`.
- `TreatWarningsAsErrors` is **on** — zero warnings allowed.

## Commands

```bash
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~LC0XX"     # one rule's tests

# Regenerate the generated catalog doc after changing rule metadata:
dotnet run --project tools/RuleCatalogDocGenerator/RuleCatalogDocGenerator.csproj -- --write
# CI runs the same with --check and fails if docs/rule-catalog.md is stale.

# Sample-diagnostics verification (CI runs this too):
dotnet run --project tools/SampleDiagnosticsVerifier/SampleDiagnosticsVerifier.csproj --configuration Release -- --frameworks net8.0 net9.0 net10.0
```

## The five-surface contract (enforced in CI)

Every rule keeps these in sync; `tests/LinqContraband.Tests/Architecture/RuleCatalogIntegrityTests.cs`
fails the build otherwise:

1. Analyzer source — `src/LinqContraband/Analyzers/<DomainFolder>/LCxxx_Name/{Name}Analyzer.cs` (+ `{Name}Fixer.cs`)
2. Tests — `tests/LinqContraband.Tests/Analyzers/LCxxx_Name/`
3. Sample — `samples/LinqContraband.Sample/Samples/LCxxx_Name/`
4. Doc — `docs/LCxxx_Name.md`
5. Catalog entry — `src/LinqContraband/Catalog/RuleCatalog.cs`

**Use `/scaffold-rule` to create a new rule** — it produces all five surfaces correctly.

## Generated files — do NOT hand-edit

- **`docs/rule-catalog.md`** is generated from `Catalog/RuleCatalog.cs`. Edit the catalog, then run
  the generator (`--write`). A PreToolUse hook blocks direct edits to this file.

## Conventions

- TDD is mandatory: failing test first (crime + innocent + edge cases), then implementation.
  See `docs/adding_new_analyzer.md`.
- Analyzers/fixers are `sealed`; file-scoped namespace `LinqContraband.Analyzers.LCxxx_Name;`.
- `Initialize` must call `EnableConcurrentExecution()` + `ConfigureGeneratedCodeAnalysis(None)`.
- Prefer `RegisterOperationAction` / `RegisterCompilationStartAction` over syntax actions.
- Reuse helpers in `src/LinqContraband/Extensions/` (`IsIQueryable`, `ReferencesParameter`, …).
- Conventional commits: `feat(LCxxx): …`, `fix(LCxxx): …`. Branches: `feat/lcxxx-…`, `fix/lcxxx-…`.

## Releases (two-PR cadence)

1. Fix PR (the behaviour change). 2. Release-prep PR — bump `<Version>`, and update **both**
`<PackageReleaseNotes>` (in `src/LinqContraband/LinqContraband.csproj`) **and** the `CHANGELOG.md`
entry referencing the **same LC ids** (a test enforces this). Publishing a **GitHub Release** then
triggers `.github/workflows/publish.yml` → pack → `dotnet nuget push`.

**Use `/release-prep`** to drive this.

## Helper agents

- `analyzer-fp-fn-hunter` — adversarially probe a rule for false positives/negatives (the main bug class here).
- `roslyn-analyzer-reviewer` — Roslyn correctness/perf review of analyzer & fixer changes before a PR.
