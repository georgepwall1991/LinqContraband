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
    public async Task SaveChangesInForeach_ShouldMoveAfterLoop()
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

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        foreach (var item in items)
        {
        }

        db.SaveChanges();
    }
}" + MockNamespace;

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task SaveChangesAsyncInFor_ShouldMoveAfterLoop()
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

        var fixedCode = Usings + @"
class Program
{
    async Task Main()
    {
        using var db = new MyDbContext();

        for (int i = 0; i < 10; i++)
        {
        }

        await db.SaveChangesAsync();
    }
}" + MockNamespace;

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task SaveChangesInWhile_ShouldMoveAfterLoop()
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
            {|LC010:db.SaveChanges()|};
            i++;
        }
    }
}" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        int i = 0;
        while (i < 10)
        {
            i++;
        }
        db.SaveChanges();
    }
}" + MockNamespace;

        await VerifyFix(test, fixedCode);
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
