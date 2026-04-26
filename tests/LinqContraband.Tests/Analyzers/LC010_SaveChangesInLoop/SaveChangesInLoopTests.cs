using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC010_SaveChangesInLoop.SaveChangesInLoopAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC010_SaveChangesInLoop;

public class SaveChangesInLoopTests
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
    public async Task TestCrime_SaveChangesInForeach_ShouldTriggerLC010()
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
            db.SaveChanges();
        }
    }
}" + MockNamespace;

        // Diagnostic should appear on db.SaveChanges()
        var expected = VerifyCS.Diagnostic("LC010")
            .WithSpan(17, 13, 17, 29)
            .WithArguments("SaveChanges");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_SaveChangesAsyncInFor_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    async Task Main()
    {
        using var db = new MyDbContext();
        
        for (int i = 0; i < 10; i++)
        {
            await db.SaveChangesAsync();
        }
    }
}" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC010")
            .WithSpan(16, 19, 16, 40)
            .WithArguments("SaveChangesAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_SaveChangesInWhile_ShouldTriggerLC010()
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
            db.SaveChanges();
            i++;
        }
    }
}" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC010")
            .WithSpan(16, 13, 16, 29)
            .WithArguments("SaveChanges");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestInnocent_SaveChangesOutsideLoop_ShouldNotTrigger()
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
            // do something
        }
        db.SaveChanges();
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_LocalFunctionDeclaredInsideLoop_ShouldNotTrigger()
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
            void SaveLater()
            {
                db.SaveChanges();
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_InheritedMethodCall_ShouldTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        // Using the base class DbContext directly
        var db = new Microsoft.EntityFrameworkCore.DbContext();
        
        while (true)
        {
            db.SaveChanges();
            break;
        }
    }
}" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC010")
            .WithSpan(17, 13, 17, 29)
            .WithArguments("SaveChanges");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
