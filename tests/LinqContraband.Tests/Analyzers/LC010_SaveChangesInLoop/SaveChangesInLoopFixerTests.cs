using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC010_SaveChangesInLoop.SaveChangesInLoopAnalyzer,
    LinqContraband.Analyzers.LC010_SaveChangesInLoop.SaveChangesInLoopFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC010_SaveChangesInLoop;

public class SaveChangesInLoopFixerTests
{
    private const string Usings = @"
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TestNamespace;
";

    private const string MockNamespace = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync() => Task.FromResult(0);
        public void Dispose() {}
    }
}

namespace TestNamespace
{
    public class MyDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
    }
}";

    [Fact]
    public async Task SaveChangesInForeach_HasNoFix()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        foreach (var item in items)
        {
            {|LC010:db.SaveChanges()|};
        }
    }
}" + MockNamespace;

        await VerifyFix(test, test);
    }

    [Fact]
    public async Task SaveChangesAsyncInFor_HasNoFix()
    {
        var test = Usings + @"
class Program
{
    async Task Main()
    {
        using var db = new MyDbContext();

        for (int i = 0; i < 10; i++)
        {
            await {|LC010:db.SaveChangesAsync()|};
        }
    }
}" + MockNamespace;

        await VerifyFix(test, test);
    }

    [Fact]
    public async Task SaveChangesInWhile_HasNoFix()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        int i = 0;
        while (i < 10)
        {
            i++;
            {|LC010:db.SaveChanges()|};
        }
    }
}" + MockNamespace;

        await VerifyFix(test, test);
    }

    [Fact]
    public async Task SaveChangesInLocalFunctionCalledFromLoop_HasNoFix()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        void SaveCurrent()
        {
            {|LC010:db.SaveChanges()|};
        }

        foreach (var item in items)
        {
            SaveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyFix(test, test);
    }

    [Fact]
    public async Task SaveChangesInDoWhile_ShouldMoveAfterLoop()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        int i = 0;
        do
        {
            i++;
            {|LC010:db.SaveChanges()|};
        }
        while (i < 10);
    }
}" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        int i = 0;
        do
        {
            i++;
        }
        while (i < 10);
        db.SaveChanges();
    }
}" + MockNamespace;

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task SaveChangesInDoWhile_MovedStatement_KeepsCrlfLineEndings()
    {
        // Forces CRLF on both the source and the expected fix regardless of the checkout's
        // line endings, so this guards cross-platform against the fixer terminating the moved
        // statement with a lone LF — which would leave mixed line endings inside a CRLF file.
        // See SyntaxTriviaExtensions.GetDocumentEndOfLine.
        var test = ToCrlf(Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        int i = 0;
        do
        {
            i++;
            {|LC010:db.SaveChanges()|};
        }
        while (i < 10);
    }
}" + MockNamespace);

        var fixedCode = ToCrlf(Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        int i = 0;
        do
        {
            i++;
        }
        while (i < 10);
        db.SaveChanges();
    }
}" + MockNamespace);

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task SaveChanges_WithControlFlowInsideLoop_HasNoFix()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        foreach (var item in new List<int> { 1, 2, 3 })
        {
            if (item == 2)
                break;

            {|LC010:db.SaveChanges()|};
        }
    }
}" + MockNamespace;

        await VerifyFix(test, test);
    }

    [Fact]
    public async Task SaveChangesInForeach_HasNoFix_BecauseLoopMayNotRun()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int>();

        foreach (var item in items)
        {
            {|LC010:db.SaveChanges()|};
        }
    }
}" + MockNamespace;

        await VerifyFix(test, test);
    }

    [Fact]
    public async Task SaveChangesAsyncInDoWhile_ShouldMoveAfterLoop()
    {
        var test = Usings + @"
class Program
{
    async Task Main()
    {
        using var db = new MyDbContext();
        int i = 0;
        do
        {
            i++;
            await {|LC010:db.SaveChangesAsync()|};
        }
        while (i < 10);
    }
}" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    async Task Main()
    {
        using var db = new MyDbContext();
        int i = 0;
        do
        {
            i++;
        }
        while (i < 10);
        await db.SaveChangesAsync();
    }
}" + MockNamespace;

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task SaveChangesInDoWhile_WithMultipleSaves_HasNoFix()
    {
        // Guardrail: when the loop body contains more than one save,
        // moving only the terminal one would silently change semantics
        // (the non-terminal save would still run per iteration). The
        // fixer must refuse rather than offer a misleading rewrite.
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        int i = 0;
        do
        {
            {|LC010:db.SaveChanges()|};
            i++;
            {|LC010:db.SaveChanges()|};
        }
        while (i < 10);
    }
}" + MockNamespace;

        await VerifyFix(test, test);
    }

    [Fact]
    public async Task SaveChangesInDoWhile_NotFinalStatement_HasNoFix()
    {
        // Guardrail: if work follows the save inside the loop body,
        // moving the save after the loop would skip the trailing work
        // on the final iteration. The fixer must refuse.
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        int i = 0;
        do
        {
            {|LC010:db.SaveChanges()|};
            i++;
        }
        while (i < 10);
    }
}" + MockNamespace;

        await VerifyFix(test, test);
    }

    [Fact]
    public async Task SaveChangesInDoWhileNestedInForLoop_HasNoFix()
    {
        // The fixer used to offer "Move SaveChanges after loop" for the
        // innermost do-while even when that do-while was itself nested in
        // another loop, leaving the save inside the outer loop and still
        // triggering LC010 after the rewrite. The fix must refuse when any
        // enclosing ancestor is also a loop.
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        for (int j = 0; j < 5; j++)
        {
            int i = 0;
            do
            {
                i++;
                {|LC010:db.SaveChanges()|};
            }
            while (i < 10);
        }
    }
}" + MockNamespace;

        await VerifyFix(test, test);
    }

    [Fact]
    public async Task SaveChangesInDoWhile_ContextDeclaredInsideLoop_HasNoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        do
        {
            using var db = new MyDbContext();
            db.SaveChanges();
        }
        while (false);
    }
}" + MockNamespace;

        await VerifyFix(test, test);
    }

    [Fact]
    public async Task SaveChangesAsOnlyStatementInDoWhile_ShouldMoveAfterLoop()
    {
        // Edge case: when the save is the only statement in the do-while
        // body, removing it leaves an empty block. The fixer must still
        // produce compiler-valid code (empty body is legal, do-while
        // semantics are preserved because the loop condition is unchanged).
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        do
        {
            {|LC010:db.SaveChanges()|};
        }
        while (false);
    }
}" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        do
        {
        }
        while (false);
        db.SaveChanges();
    }
}" + MockNamespace;

        await VerifyFix(test, fixedCode);
    }

    // Normalizes every line ending to CRLF so a test can assert line-ending behaviour
    // independently of how the repository is checked out (autocrlf on Windows, LF on Unix).
    private static string ToCrlf(string value) =>
        value.Replace("\r\n", "\n").Replace("\n", "\r\n");

    [Fact]
    public async Task FixAll_RewritesAllMultipleDoWhileSaveChangesCases()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        int i = 0;
        do
        {
            i++;
            {|#0:db.SaveChanges()|};
        }
        while (i < 10);

        int j = 0;
        do
        {
            j++;
            {|#1:db.SaveChanges()|};
        }
        while (j < 5);
    }
}" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        int i = 0;
        do
        {
            i++;
        }
        while (i < 10);
        db.SaveChanges();

        int j = 0;
        do
        {
            j++;
        }
        while (j < 5);
        db.SaveChanges();
    }
}" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "MoveSaveChangesAfterLoop"
        };

        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC010", DiagnosticSeverity.Warning)
                .WithLocation(0));
        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC010", DiagnosticSeverity.Warning)
                .WithLocation(1));

        await testObj.RunAsync();
    }

    private static async Task VerifyFix(string test, string fixedCode)
    {
        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            CompilerDiagnostics = CompilerDiagnostics.None
        };

        await testObj.RunAsync();
    }
}
