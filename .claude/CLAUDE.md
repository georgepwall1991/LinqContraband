# LinqContraband: notes for Claude

Roslyn analyzer + code-fix package for EF Core LINQ performance and safety (`LinqContraband` on NuGet). `AGENTS.md` is the router for local desktop agents and points at Mac-only tooling; in Claude Code on the web, use this file and the skills in `.claude/skills/`. A root `CLAUDE.md` is git-ignored so maintainers can keep personal notes there.

## Build and test

The SessionStart hook (`.claude/hooks/session-start.sh`) installs the .NET 10 SDK from the Ubuntu archive and restores packages, because the sandbox cannot reach Microsoft's download host. Only the net10.0 runtime is installed, so always pass `-f net10.0` to `dotnet test`; CI covers net8.0 and net9.0.

```bash
dotnet build                                                        # ~20s
dotnet test --no-build -f net10.0                                   # full suite, ~2 min
dotnet test --no-build -f net10.0 --filter "FullyQualifiedName~LC044"
dotnet run --project tools/RuleCatalogDocGenerator/RuleCatalogDocGenerator.csproj -- --check   # --write to regenerate docs/rule-catalog.md
dotnet run --project tools/SampleDiagnosticsVerifier/SampleDiagnosticsVerifier.csproj --configuration Release -- --frameworks net8.0 net9.0 net10.0
python3 scripts/release_info.py check                               # csproj Version has a CHANGELOG section
```

These are the same checks `.github/workflows/dotnet.yml` runs. Run them before every push instead of relying on CI to find failures. CI also fails if line coverage drops below 75%.

## Load-bearing facts

- The analyzer compiles against **Roslyn 4.3.0** (`Microsoft.CodeAnalysis.CSharp` in `src/LinqContraband/LinqContraband.csproj`) on purpose, so it loads in older VS/SDK hosts. Never bump it. Tests and tools use 4.14.0.
- `TreatWarningsAsErrors` is on. `EnforceExtendedAnalyzerRules` is on, so analyzers cannot use banned APIs (file IO, etc.).
- `src/LinqContraband/Catalog/RuleCatalog*.cs` is the source of truth for every rule. Each rule needs the analyzer folder, tests, a sample under `samples/LinqContraband.Sample/Samples/`, and `docs/LCxxx_Name.md`. A rule without a fixer needs a written rationale in the catalog. `RuleCatalogIntegrityTests` enforces this.
- Any code shape that newly reports must be added to that rule's fixer-coverage corpus in the same change (see `CONTRIBUTING.md`).
- `docs/analyzer-health.md` scores every rule and is the backlog for hardening work. Update its row and the suite test count when you change a rule.
- `docs/` is the GitHub Pages site (Jekyll). `README.md` is packed into the nupkg as the NuGet readme.
- Do not run CSharpier over existing files. Most of the codebase is not CSharpier-formatted, and a mass reformat would conflict with every open branch. Match the surrounding style instead.

## Workflow

- One focused change per branch and PR, conventional commits (`feat(LC0xx):`, `fix(LC0xx):`, `docs:`, `chore:`, `ci:`), squash-merge to `master`.
- Every behavior change gets an entry in `CHANGELOG.md`.
- Releases follow the "Releasing" section of `CONTRIBUTING.md`: a `chore: release X.Y.Z` PR bumps `<Version>` and `<PackageReleaseNotes>`, turns `## [Unreleased]` into the version's CHANGELOG section, and updates the health doc's release metadata. `release.yml` then tags, creates the GitHub Release and publishes. Never create tags or releases by hand.
- For analyzer hardening, use the `analyzer-hardening` skill.
