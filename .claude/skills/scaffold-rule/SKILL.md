---
name: scaffold-rule
description: Scaffold a new LCxxx analyzer rule with all five contract surfaces (analyzer, tests, sample, doc, RuleCatalog entry) kept in sync, following the repo's TDD workflow, then regenerate the rule catalog and run the tests. Use when adding a new EF Core analyzer rule to LinqContraband.
disable-model-invocation: true
---

# Scaffold a New LinqContraband Rule

Creates a complete, contract-compliant LCxxx rule. The repo enforces a strict
**five-surface contract** via `tests/LinqContraband.Tests/Architecture/RuleCatalogIntegrityTests.cs`
— this skill produces all five surfaces so that test passes.

## Inputs

Parse from the user's invocation (ask only if missing):

- **Rule id** — `LCxxx` (next free sequential number; current max is LC044, so default to the next one)
- **Name** — PascalCase descriptive name, e.g. `NoTrackingForReads`
- **Domain** — one of the 8 neighborhoods (folder name ↔ catalog `domain` string):

  | Folder (`Analyzers/<Folder>/`)        | Catalog `domain` string              |
  | ------------------------------------- | ------------------------------------ |
  | `QueryShapeAndTranslation`            | `Query Shape & Translation`          |
  | `MaterializationAndProjection`        | `Materialization & Projection`       |
  | `LoadingAndIncludes`                  | `Loading & Includes`                 |
  | `ExecutionAndAsync`                   | `Execution & Async`                  |
  | `ChangeTrackingAndContextLifetime`    | `Change Tracking & Context Lifetime` |
  | `BulkOperationsAndSetBasedWrites`     | `Bulk Operations & Set-Based Writes` |
  | `SchemaAndModeling`                   | `Schema & Modeling`                  |
  | `RawSqlAndSecurity`                   | `Raw SQL & Security`                 |

- **Has fixer?** — yes/no. If **no**, get a one-line rationale (required by the catalog).

## First: read a real template

Always base new files on an existing rule rather than inventing structure. Read these
verbatim before writing anything (LC001 is the canonical small example):

- `src/LinqContraband/Analyzers/QueryShapeAndTranslation/LC001_LocalMethod/LocalMethodAnalyzer.cs`
- `src/LinqContraband/Analyzers/QueryShapeAndTranslation/LC001_LocalMethod/LocalMethodFixer.cs`
- `tests/LinqContraband.Tests/Analyzers/LC001_LocalMethod/LocalMethodSmugglerTests.cs`
- `samples/LinqContraband.Sample/Samples/LC001_LocalMethod/LocalMethodSample.cs`
- `docs/LC001_LocalMethod.md`
- The LC001 entry in `src/LinqContraband/Catalog/RuleCatalog.cs`

Also skim `docs/adding_new_analyzer.md` (the canonical guide) and reuse the shared
helpers in `src/LinqContraband/Extensions/` (e.g. `IsIQueryable()`, `ReferencesParameter()`).

## TDD workflow (mandatory — tests before implementation)

Follow the repo's red→green order. Do NOT write analyzer logic before a failing test exists.

1. **Failing analyzer test** — `tests/LinqContraband.Tests/Analyzers/LCxxx_Name/{Name}Tests.cs`
   - File-scoped namespace `LinqContraband.Tests.Analyzers.LCxxx_Name;`
   - `using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<LinqContraband.Analyzers.LCxxx_Name.{Name}Analyzer>;`
   - Include `Usings` + `MockNamespace` consts (copy the EF-Core-like stubs from LC001).
   - Write at minimum a **crime** case (`.WithSpan(...).WithArguments(...)`), an **innocent**
     case (no diagnostic), and one **edge** case (static vs instance, nested lambda, params, etc.).
2. **Analyzer** — `src/LinqContraband/Analyzers/<Folder>/LCxxx_Name/{Name}Analyzer.cs`
   - File-scoped namespace `LinqContraband.Analyzers.LCxxx_Name;`
   - `[DiagnosticAnalyzer(LanguageNames.CSharp)] public sealed class {Name}Analyzer : DiagnosticAnalyzer`
   - `public const string DiagnosticId = "LCxxx";`, `Category`, `Title`/`MessageFormat`/`Description` as `LocalizableString`, a static `DiagnosticDescriptor Rule`, `SupportedDiagnostics`.
   - `Initialize` MUST call `context.EnableConcurrentExecution();` and
     `context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);`
   - **Prefer `RegisterOperationAction` / `RegisterCompilationStartAction` over `RegisterSyntaxNodeAction`** (semantic accuracy + per-compilation symbol caching).
3. **Green** — `dotnet test --filter "FullyQualifiedName~LCxxx"` until the analyzer tests pass.
4. **(If fixer) failing fixer test** — `{Name}FixerTests.cs` using the `CSharpCodeFixTest<Analyzer, Fixer, XUnitVerifier>` pattern (TestCode/FixedCode).
5. **(If fixer) fixer** — `{Name}Fixer.cs`: `[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof({Name}Fixer))] [Shared]`, `FixableDiagnosticIds`, `GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer`, `RegisterCodeFixesAsync`.

## Then: complete the contract (all five surfaces)

6. **Sample** — `samples/LinqContraband.Sample/Samples/LCxxx_Name/{Name}Sample.cs` showing a violation (and ideally the corrected form).
7. **Doc** — `docs/LCxxx_Name.md` matching the LC001 heading style (Goal / The Problem / Example Violation / The Fix).
8. **RuleCatalog entry** — add to `src/LinqContraband/Catalog/RuleCatalog.cs`. The `RuleCatalogEntry` constructor is:
   ```csharp
   new RuleCatalogEntry(
       id: "LCxxx",
       slug: "LCxxx_Name",
       title: "Human readable title",
       category: "Performance",                 // or Correctness / Security / Design
       domain: "Query Shape & Translation",     // the human-readable string from the table above
       severity: DiagnosticSeverity.Warning,
       analyzerTypeName: "{Name}Analyzer",
       fixerTypeName: "{Name}Fixer",            // null if no fixer
       documentationPath: "docs/LCxxx_Name.md",
       samplePath: "samples/LinqContraband.Sample/Samples/LCxxx_Name/{Name}Sample.cs",
       analyzerSourcePath: "src/LinqContraband/Analyzers/<Folder>/LCxxx_Name",
       hasCodeFix: true,                        // false if no fixer
       noCodeFixRationale: null)                // required non-null string when hasCodeFix is false
   ```

## Regenerate + verify (do NOT hand-edit docs/rule-catalog.md)

9. Regenerate the catalog doc:
   ```bash
   dotnet run --project tools/RuleCatalogDocGenerator/RuleCatalogDocGenerator.csproj -- --write
   ```
10. Run the full gate the way CI does:
    ```bash
    dotnet build
    dotnet test
    dotnet run --project tools/SampleDiagnosticsVerifier/SampleDiagnosticsVerifier.csproj --configuration Release -- --frameworks net8.0 net9.0 net10.0
    ```
    Confirm `RuleCatalogIntegrityTests` passes (proves all five surfaces are wired correctly).

## Finish

Report: the files created, the catalog entry added, and the test/verifier results.
Suggest a branch name (`feat/lcxxx-short-name`) and a conventional commit
(`feat(LCxxx): <what the rule catches> — <why it matters>`). Do not commit unless asked.
