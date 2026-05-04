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
    public async Task TestCrime_SaveChangesInDoWhile_ShouldTriggerLC010()
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

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_SaveChangesAsyncInAwaitForeach_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    async Task Main(IAsyncEnumerable<int> items)
    {
        using var db = new MyDbContext();
        await foreach (var item in items)
        {
            await {|LC010:db.SaveChangesAsync()|};
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
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
    public async Task TestCrime_LocalFunctionCalledInsideLoop_ShouldTriggerLC010()
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

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_SaveChangesInsideRetryLoopWithBreakAfterSuccess_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                db.SaveChanges();
                break;
            }
            catch (Exception)
            {
                if (attempt == 2)
                    throw;
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_SaveChangesAsyncInsideRetryLoopWithBreakAfterSuccess_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    async Task Main()
    {
        using var db = new MyDbContext();

        while (true)
        {
            try
            {
                await db.SaveChangesAsync();
                break;
            }
            catch (Exception)
            {
                await Task.Delay(1);
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_SaveChangesInsideRetryLoopWithReturnAfterSuccess_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    bool Main()
    {
        using var db = new MyDbContext();

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                db.SaveChanges();
                return true;
            }
            catch (Exception)
            {
                if (attempt == 2)
                    throw;
            }
        }

        return false;
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_SaveChangesInsideCatchGuardedLoopWithConditionalReturn_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    bool Main(bool done)
    {
        using var db = new MyDbContext();

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                {|LC010:db.SaveChanges()|};
                if (done)
                    return true;
            }
            catch (Exception)
            {
            }
        }

        return false;
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_SaveChangesInsideCatchGuardedLoopWithoutBreak_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                {|LC010:db.SaveChanges()|};
            }
            catch (Exception)
            {
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_LocalFunctionCalledOutsideLoop_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();

        void SaveOnce()
        {
            db.SaveChanges();
        }

        SaveOnce();
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_LambdaInsideLocalFunctionCalledInLoop_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        void RegisterSave()
        {
            Action saveLater = () => db.SaveChanges();
        }

        foreach (var item in items)
        {
            RegisterSave();
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
