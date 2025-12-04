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
        LC001_LocalMethodSmuggler/
            LocalMethodSmugglerAnalyzer.cs
            LocalMethodSmugglerFixer.cs
        LC002_.../
    Extensions/
        AnalysisExtensions.cs

tests/LinqContraband.Tests/
    Analyzers/
        LC001_LocalMethodSmuggler/
            LocalMethodSmugglerTests.cs
            LocalMethodSmugglerFixerTests.cs
```

## Adding a New Analyzer

We follow a strict **Test-Driven Development (TDD)** workflow for all analyzers. See [docs/adding_new_analyzer.md](docs/adding_new_analyzer.md) for the complete step-by-step guide.

### Quick Summary

1. **Write failing tests first** - Create test cases that should trigger your diagnostic
2. **Implement the analyzer** - Write logic to detect the anti-pattern
3. **Verify tests pass** - Ensure your implementation works
4. **Add a code fixer** (optional) - Implement automatic fix if applicable
5. **Document the rule** - Add to README and create a detailed doc if needed

### Naming Conventions

- **Diagnostic ID**: `LC0XX` (sequential, e.g., LC017, LC018)
- **Directory**: `src/LinqContraband/Analyzers/LCxxx_DescriptiveName/`
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
- [ ] Code follows existing patterns in the codebase
- [ ] README is updated if adding a new analyzer
- [ ] Commit messages follow conventional format

## Code Style

- We use `.editorconfig` for consistent formatting
- Enable `TreatWarningsAsErrors` - fix all warnings
- Prefer `RegisterOperationAction` over `RegisterSyntaxNodeAction` for semantic analysis
- Use extension methods from `AnalysisExtensions.cs` when applicable
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
