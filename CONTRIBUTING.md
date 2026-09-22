# Contributing to LinqContraband

Thank you for your interest in contributing to LinqContraband! This document provides guidelines for contributing to the project.

## Development Setup

### Prerequisites

- .NET 8.0 SDK or later
- An IDE with Roslyn support (Visual Studio 2022, JetBrains Rider, or VS Code with C# extension)

### Getting Started

1. Fork and clone the repository:
   ```bash
   git clone https://github.com/YOUR_USERNAME/LinqContraband.git
   cd LinqContraband
   ```

2. Restore dependencies and build:
   ```bash
   dotnet restore
   dotnet build
   ```

3. Run tests to verify your setup:
   ```bash
   dotnet test
   ```

## Project Structure

```
src/LinqContraband/
    Analyzers/
        QueryShapeAndTranslation/
            LC001_LocalMethod/
                LocalMethodAnalyzer.cs
                LocalMethodFixer.cs
        MaterializationAndProjection/
            LC002_.../
    Catalog/
        RuleCatalog.cs
        RuleCatalogEntry.cs
    Extensions/
        AnalysisExtensions.cs
        InvocationAnalysisExtensions.cs
        OperationReferenceExtensions.cs
        OperationTraversalExtensions.cs
        SymbolAnalysisExtensions.cs

tools/
    RuleCatalogDocGenerator/
        Program.cs

tests/LinqContraband.Tests/
    Analyzers/
        LC001_LocalMethod/
            LocalMethodSmugglerTests.cs
            LocalMethodFixerTests.cs
    Architecture/
        RuleCatalogIntegrityTests.cs
```

## Adding a New Analyzer

We follow a strict **Test-Driven Development (TDD)** workflow for all analyzers. See [docs/adding_new_analyzer.md](docs/adding_new_analyzer.md) for the complete step-by-step guide.

## Repository Contract

Every rule is now governed by the central catalog in `src/LinqContraband/Catalog/RuleCatalog.cs`. A valid rule contribution keeps all mirrored surfaces in sync:

- analyzer source folder under the domain path recorded in `RuleCatalog` (for example `src/LinqContraband/Analyzers/QueryShapeAndTranslation/LCxxx_Name/`)
- tests under `tests/LinqContraband.Tests/Analyzers/LCxxx_Name/`
- sample under `samples/LinqContraband.Sample/Samples/LCxxx_Name/`
- docs page at `docs/LCxxx_Name.md`

If a rule intentionally has no fixer, record the rationale in the catalog. `tests/LinqContraband.Tests/Architecture/RuleCatalogIntegrityTests.cs` enforces this contract in CI.

`docs/rule-catalog.md`, the rule table in `README.md`, and `docs/_data/rules.json` (the severity, code-fix and config box on each rule page) are generated from `RuleCatalog`. Regenerate them locally with:
```bash
dotnet run --project tools/RuleCatalogDocGenerator/RuleCatalogDocGenerator.csproj -- --write
```
CI runs the same tool with `--check` and fails if any of them is stale.

Each rule's `helpLinkUri` (the link an IDE opens from a diagnostic) must be `RuleCatalog.DocumentationSiteUri` plus the docs page name, for example `RuleCatalog.DocumentationSiteUri + "LC001_LocalMethod.html"`. The README is also the NuGet package readme, so keep rule write-ups in `docs/LCxxx_Name.md` rather than in the README.

### Quick Summary

1. **Write failing tests first** - Create test cases that should trigger your diagnostic
2. **Implement the analyzer** - Write logic to detect the anti-pattern
3. **Verify tests pass** - Ensure your implementation works
4. **Add a code fixer** (optional) - Implement automatic fix if applicable
5. **Update the catalog contract** - Add the new rule to `src/LinqContraband/Catalog/RuleCatalog.cs`, including domain, docs path, sample path, and code-fix metadata
6. **Document the rule** - Add the rule doc and keep the taxonomy docs current

### Naming Conventions

- **Diagnostic ID**: `LC0XX` (sequential, e.g., LC017, LC018)
- **Source directory**: `src/LinqContraband/Analyzers/<DomainFolder>/LCxxx_DescriptiveName/`
- **Analyzer class**: `{Name}Analyzer.cs`
- **Fixer class**: `{Name}Fixer.cs`
- **Test classes**: `{Name}Tests.cs`, `{Name}FixerTests.cs`

## Branch Naming

Use descriptive branch names with prefixes:

- `feat/lc017-whole-entity-projection` - New analyzer or feature
- `fix/lc009-false-positive` - Bug fix for existing analyzer
- `docs/update-readme` - Documentation changes
- `chore/update-dependencies` - Maintenance tasks

## Commit Messages

Follow [Conventional Commits](https://www.conventionalcommits.org/) format:

```
feat(LC017): add whole entity projection analyzer
fix(LC009): handle edge case with async methods
docs: update README with new analyzer
chore: bump Roslyn to 4.3.1
test: add edge case tests for LC015
```

## Pull Request Checklist

Before submitting a PR, ensure:

- [ ] All tests pass (`dotnet test`)
- [ ] Build succeeds with no warnings (`dotnet build`)
- [ ] New analyzers have both "crime" and "innocent" test cases
- [ ] **If a change makes a new code shape report, that shape was added to the analyzer's fixer-coverage corpus in the same change.** For LC045 that is `MissingIncludeFixerCoverageContractTests`. Nothing enforces this mechanically, and it has been missed twice — a shape that reports but is never asked whether it has a compiling fix is how 5.7.28 shipped a code fix that produced uncompilable code.
- [ ] Code follows existing patterns in the codebase
- [ ] Rule docs page and generated rule catalog/README table are updated if adding a new analyzer
- [ ] RuleCatalog entry was added or updated
- [ ] Architecture integrity tests pass
- [ ] Commit messages follow conventional format

## Releasing

Releases are driven by the version in `src/LinqContraband/LinqContraband.csproj`:

1. Open a `chore: release X.Y.Z` PR that bumps `<Version>` and `<PackageReleaseNotes>` and renames the `## [Unreleased]` section of `CHANGELOG.md` to `## [X.Y.Z] - YYYY-MM-DD`. CI fails if the csproj version has no CHANGELOG section (`python3 scripts/release_info.py check`).
2. Merge it. When **Build and Test** passes on `master`, `.github/workflows/release.yml` creates the annotated `vX.Y.Z` tag and a GitHub Release whose notes are that CHANGELOG section, then dispatches `.github/workflows/publish.yml`.
3. `publish.yml` refuses to publish if the tag is not `v<csproj Version>`, runs the full build and tests, and pushes the package to NuGet with [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) (no stored API key).

Do not create tags or releases by hand. Green `master` builds whose version is already released are a no-op. To retry a failed publish, run **Publish to NuGet** manually with the tag; tick `force_publish` to skip the 12-hour cooldown between releases.

## Code Style

- We use `.editorconfig` for consistent formatting
- Enable `TreatWarningsAsErrors` - fix all warnings
- Prefer `RegisterOperationAction` over `RegisterSyntaxNodeAction` for semantic analysis
- Use the shared analysis helpers in `src/LinqContraband/Extensions/` when applicable
- Enable concurrent execution and skip generated code analysis:
  ```csharp
  context.EnableConcurrentExecution();
  context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
  ```

## Testing Guidelines

- Each analyzer should have tests covering:
  - **Crime cases**: Code patterns that SHOULD trigger the diagnostic
  - **Innocent cases**: Similar code that should NOT trigger
  - **Edge cases**: Boundary conditions and special scenarios
- Use the `MockNamespace` pattern to simulate EF Core entities and DbContext
- Verify diagnostic spans point to the correct location

## Questions or Issues?

- Open a [GitHub Issue](https://github.com/georgepwall1991/LinqContraband/issues) for bugs or feature requests
- Discussions are welcome for design questions before implementation

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
