# Adding a New Analyzer and Fixer

This guide outlines the process for adding a new Roslyn analyzer and code fix provider to the `LinqContraband` project, following our Test-Driven Development (TDD) workflow.

## 1. Naming and Structure

We follow a strict naming convention and directory structure.

*   **Diagnostic ID**: `LCxxx` (e.g., `LC001`, `LC002`).
*   **Directory**: `src/LinqContraband/Analyzers/LCxxx_Name/`
*   **Test Directory**: `tests/LinqContraband.Tests/Analyzers/LCxxx_Name/`

### Example Structure
```
src/LinqContraband/Analyzers/LC003_NewRule/
    ├── NewRuleAnalyzer.cs
    └── NewRuleFixer.cs

tests/LinqContraband.Tests/Analyzers/LC003_NewRule/
    ├── NewRuleTests.cs
    └── NewRuleFixerTests.cs
```

## 2. TDD Workflow

We use TDD to ensure our analyzers and fixers work as expected. The general workflow is:

1.  **Write a failing Analyzer test**: Create a test case that should trigger the diagnostic.
2.  **Implement the Analyzer**: Write the logic to detect the issue.
3.  **Verify Analyzer passes**: Run the test to ensure it passes.
4.  **Write a failing Fixer test**: Create a test case for the code fix.
5.  **Implement the Fixer**: Write the logic to apply the fix.
6.  **Verify Fixer passes**: Run the test to ensure the fix is applied correctly.

## 3. Step-by-Step Implementation

### Step 1: Create the Analyzer Test

Create a new test file `tests/LinqContraband.Tests/Analyzers/LCxxx_Name/NameTests.cs`.

Use the `VerifyCS` helper to write your tests.

```csharp
using System.Threading.Tasks;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<LinqContraband.YourAnalyzer>;

namespace LinqContraband.Tests
{
    public class NameTests
    {
        // Common setup for tests (usings, mock classes)
        private const string Usings = @"...";
        private const string MockNamespace = @"...";

        [Fact]
        public async Task TestCrime_Scenario_ShouldTriggerLCxxx()
        {
            var test = Usings + @"
class Program
{
    void Main()
    {
        // Code that should trigger the analyzer
    }
}" + MockNamespace;

            // Define expected diagnostic
            var expected = VerifyCS.Diagnostic("LCxxx")
                .WithSpan(startLine, startChar, endLine, endChar)
                .WithArguments("ArgumentName");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Fact]
        public async Task TestInnocent_Scenario_ShouldNotTrigger()
        {
            var test = Usings + @"
class Program
{
    void Main()
    {
        // Code that should NOT trigger the analyzer
    }
}" + MockNamespace;

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
```

### Step 2: Implement the Analyzer

Create `src/LinqContraband/Analyzers/LCxxx_Name/NameAnalyzer.cs`.

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class NameAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "LCxxx";
        // Define Title, MessageFormat, Description, Category...

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(...);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            // Register your action, e.g., context.RegisterOperationAction(...)
        }

        private void AnalyzeOperation(OperationAnalysisContext context)
        {
            // Implementation logic
            // 1. Check constraints
            // 2. Report diagnostic if needed
            // context.ReportDiagnostic(Diagnostic.Create(Rule, location, args));
        }
    }
}
```

### Step 3: Create the Fixer Test

Create `tests/LinqContraband.Tests/Analyzers/LCxxx_Name/NameFixerTests.cs`.

```csharp
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<LinqContraband.YourAnalyzer, LinqContraband.YourFixer, Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests
{
    public class NameFixerTests
    {
        // ... Usings and MockNamespace ...

        [Fact]
        public async Task FixCrime_Scenario_ShouldApplyFix()
        {
            var test = @"... code before fix ...";
            var fixedCode = @"... code after fix ...";

            var testObj = new CodeFixTest
            {
                TestCode = test,
                FixedCode = fixedCode,
                // Set CompilerDiagnostics.None if the code might not compile (e.g. incomplete snippets)
                CompilerDiagnostics = CompilerDiagnostics.None 
            };
            
            testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LCxxx", DiagnosticSeverity.Warning)
                .WithSpan(...)
                .WithArguments(...));

            await testObj.RunAsync();
        }
    }
}
```

### Step 4: Implement the Fixer

Create `src/LinqContraband/Analyzers/LCxxx_Name/NameFixer.cs`.

```csharp
using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace LinqContraband
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NameFixer)), Shared]
    public class NameFixer : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(NameAnalyzer.DiagnosticId);

        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            // Locate the node to fix
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var diagnostic = context.Diagnostics.First();
            
            // Register the fix
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Fix description",
                    createChangedDocument: c => ApplyFixAsync(context.Document, node, c),
                    equivalenceKey: "Key"),
                diagnostic);
        }

        private async Task<Document> ApplyFixAsync(Document document, SyntaxNode node, CancellationToken cancellationToken)
        {
            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
            
            // Perform syntax modifications
            
            return editor.GetChangedDocument();
        }
    }
}
```

## 4. Key Considerations

*   **IQueryable Context**: Ensure your analyzer checks that the code is executing within an `IQueryable` expression tree. Use `IsIQueryable()` extension method if available or check types.
*   **Operation vs Syntax**: Prefer `RegisterOperationAction` over `RegisterSyntaxNodeAction` for better semantic understanding (e.g., resolving method calls regardless of syntax).
*   **Testing**:
    *   Include `Usings` and `MockNamespace` to simulate a realistic environment (Entity Framework-like classes).
    *   Test both positive cases (should trigger) and negative cases (should not trigger).
    *   Test edge cases (nested lambdas, static methods vs instance methods).

## 5. Running Tests

Run all tests from the root directory:

```bash
dotnet test
```

Or run specific tests for your analyzer:

```bash
dotnet test --filter "FullyQualifiedName~LCxxx"
```

