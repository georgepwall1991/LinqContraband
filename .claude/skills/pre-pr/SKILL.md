---
name: pre-pr
description: Run the LinqContraband CI gate locally before opening a PR — catalog --check, build, sample-diagnostics verifier (net8/9/10), tests, and the 75% coverage threshold — stopping at the first failure and reporting which surface broke. Use before pushing a branch or opening a PR.
disable-model-invocation: true
---

# Local CI Gate (pre-PR)

Runs the **same checks `.github/workflows/dotnet.yml` runs**, in the same order, locally — so a
red CI is caught in ~2 minutes instead of a push round-trip. This matters because the repo uses a
two-PR fix→release cadence: a failed gate after pushing wastes a whole cycle.

Run each step in order. **Stop at the first failure**, report exactly which surface broke and the
relevant output, and suggest the fix. Do not continue past a failure.

## Steps (mirror of `dotnet.yml`)

1. **Restore**
   ```bash
   dotnet restore
   ```

2. **Generated catalog is in sync** (CI runs `--check`; never hand-edit `docs/rule-catalog.md`)
   ```bash
   dotnet run --project tools/RuleCatalogDocGenerator/RuleCatalogDocGenerator.csproj -- --check
   ```
   If this fails, the catalog doc is stale → regenerate with `-- --write` (the source of truth is
   `src/LinqContraband/Catalog/RuleCatalog.cs`), then restart the gate.

3. **Build** (`TreatWarningsAsErrors` is on — a warning is a failure)
   ```bash
   dotnet build --no-restore
   ```

4. **Sample diagnostics verifier** (proves each sample still emits exactly its rule's diagnostics)
   ```bash
   dotnet run --project tools/SampleDiagnosticsVerifier/SampleDiagnosticsVerifier.csproj --configuration Release -- --frameworks net8.0 net9.0 net10.0
   ```

5. **Tests** — includes `RuleCatalogIntegrityTests` (the five-surface contract) and the
   CHANGELOG ↔ PackageReleaseNotes consistency test.
   ```bash
   dotnet test --no-build --verbosity normal
   ```
   To iterate on one rule first: `dotnet test --filter "FullyQualifiedName~LC0XX"`.

6. **Coverage threshold** (CI hard-fails below **75%** line coverage). Only run when you want the
   full gate; it re-runs tests with collection:
   ```bash
   dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage
   ```
   Then check the `line-rate` in `coverage/**/coverage.cobertura.xml` is ≥ 0.75. If a changed
   analyzer dropped coverage, add the missing crime/innocent/edge test before pushing.

## Finish

Report a concise PASS/FAIL per step. On all-green, say the branch is clear for a PR and remind the
user of the conventional-commit / branch-name conventions (`feat(LCxxx):` / `fix(LCxxx):`,
`feat/lcxxx-…` / `fix/lcxxx-…`). Do not commit, push, or open the PR unless asked.
