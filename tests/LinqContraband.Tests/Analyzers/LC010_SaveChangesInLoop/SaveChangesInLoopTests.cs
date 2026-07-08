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
    public async Task TestInnocent_FreshContextCreatedInsideLoop_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var items = new List<int> { 1, 2, 3 };

        foreach (var item in items)
        {
            using var db = new MyDbContext();
            db.SaveChanges();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ContextAliasDeclaredInsideLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var sharedDb = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        foreach (var item in items)
        {
            var current = sharedDb;
            {|LC010:current.SaveChanges()|};
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ContextDeclaredInForInitializer_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        bool keepGoing = true;

        for (var db = new MyDbContext(); keepGoing; keepGoing = false)
        {
            {|LC010:db.SaveChanges()|};
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_FreshContextReassignedToSharedContextInsideLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var sharedDb = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        foreach (var item in items)
        {
            var db = new MyDbContext();
            db = sharedDb;
            {|LC010:db.SaveChanges()|};
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_FreshContextReassignedByCalledLocalFunctionBeforeSave_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var sharedDb = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        foreach (var item in items)
        {
            var db = new MyDbContext();
            void UseShared()
            {
                db = sharedDb;
            }

            UseShared();
            {|LC010:db.SaveChanges()|};
        }
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
    public async Task TestCrime_LambdaAssignedToDelegateCalledInsideLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action saveCurrent = () => {|LC010:db.SaveChanges()|};

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegatePassedToLocalInvokerInsideLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => {|LC010:db.SaveChanges()|};

        void Invoke(Action callback)
        {
            callback();
        }

        foreach (var item in items)
        {
            Invoke(saveCurrent);
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_FreshContextDelegatePassedToLocalInvokerInsideLoop_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var items = new List<int> { 1, 2, 3 };

        void Invoke(Action callback)
        {
            callback();
        }

        foreach (var item in items)
        {
            using var db = new MyDbContext();
            Action saveCurrent = () => db.SaveChanges();
            Invoke(saveCurrent);
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_LambdaAssignedToDelegateCalledViaConditionalAccessInsideLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action saveCurrent = () => {|LC010:db.SaveChanges()|};

        foreach (var item in items)
        {
            saveCurrent?.Invoke();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateAddedWithPlusEqualsCalledInsideLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action? saveCurrent = null;

        saveCurrent += () => {|LC010:db.SaveChanges()|};

        foreach (var item in items)
        {
            saveCurrent?.Invoke();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateCombinedWithSelfAssignmentCalledInsideLoop_ShouldTriggerLC010()
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

        void Other()
        {
        }

        Action saveCurrent = SaveCurrent;
        saveCurrent = saveCurrent + Other;

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_SelfCombiningDelegateAssignmentWithLambdaRhsCalledInsideLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        saveCurrent = saveCurrent + (() => {|LC010:db.SaveChanges()|});

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_SelfCombiningDelegateAssignmentWithMethodGroupRhsCalledInsideLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        void SaveCurrent()
        {
            {|LC010:db.SaveChanges()|};
        }

        saveCurrent = saveCurrent + SaveCurrent;

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_LambdaAssignedToDelegateCalledInsideConstructorLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    Program()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action saveCurrent = () => {|LC010:db.SaveChanges()|};

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_LocalFunctionAssignedToDelegateCalledInsideLoop_ShouldTriggerLC010()
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

        Action saveCurrent = SaveCurrent;

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateRemovedWithMinusEqualsBeforeLoop_ShouldNotTrigger()
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
            db.SaveChanges();
        }

        Action saveCurrent = SaveCurrent;
        saveCurrent -= SaveCurrent;

        foreach (var item in items)
        {
            saveCurrent?.Invoke();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateRemovedOnceAfterDuplicateSubscription_ShouldTriggerLC010()
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

        Action saveCurrent = SaveCurrent;
        saveCurrent += SaveCurrent;
        saveCurrent -= SaveCurrent;

        foreach (var item in items)
        {
            saveCurrent?.Invoke();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateStillReportsWhenDifferentDelegateRemovedBeforeLoop_ShouldTriggerLC010()
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

        void Other()
        {
        }

        Action saveCurrent = SaveCurrent;
        saveCurrent -= Other;

        foreach (var item in items)
        {
            saveCurrent?.Invoke();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_LocalFunctionAssignedToDelegateCalledInsideConstructorLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    Program()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        void SaveCurrent()
        {
            {|LC010:db.SaveChanges()|};
        }

        Action saveCurrent = SaveCurrent;

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_SaveChangesMethodGroupAssignedToDelegateCalledInsideLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Func<int> saveCurrent = {|LC010:db.SaveChanges|};

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_SaveChangesAsyncMethodGroupAssignedToDelegateCalledInsideLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    async Task Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Func<Task<int>> saveCurrent = {|LC010:db.SaveChangesAsync|};

        foreach (var item in items)
        {
            await saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateCalledInsideLocalFunctionLoopAfterAssignment_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        void RunSaves()
        {
            foreach (var item in items)
            {
                saveCurrent();
            }
        }

        saveCurrent = () => {|LC010:db.SaveChanges()|};
        RunSaves();
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateCalledInsideLocalFunctionAfterLaterAssignmentAndLoopCall_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        void RunSave()
        {
            saveCurrent();
        }

        saveCurrent = () => {|LC010:db.SaveChanges()|};

        foreach (var item in items)
        {
            RunSave();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateInvokedInsideLocalFunctionCalledFromLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        void RunSave()
        {
            Action saveCurrent = () => {|LC010:db.SaveChanges()|};
            saveCurrent();
        }

        foreach (var item in items)
        {
            RunSave();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateDeclaredInsideCalledLocalFunctionLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        void RunSaves()
        {
            Action saveCurrent = () => {|LC010:db.SaveChanges()|};

            foreach (var item in items)
            {
                saveCurrent();
            }
        }

        RunSaves();
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateCalledInsideOuterDelegateCalledFromLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action saveCurrent = () => {|LC010:db.SaveChanges()|};

        Action runSaves = () =>
        {
            saveCurrent();
        };

        foreach (var item in items)
        {
            runSaves();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateCalledInsideOuterDelegateLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action saveCurrent = () => {|LC010:db.SaveChanges()|};
        Action runSaves = () =>
        {
            foreach (var item in items)
            {
                saveCurrent();
            }
        };

        runSaves();
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_OuterDelegateLoopWithFreshOuterContext_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var batches = new List<int> { 1, 2, 3 };
        var items = new List<int> { 1, 2, 3 };

        foreach (var batch in batches)
        {
            using var db = new MyDbContext();
            Action saveCurrent = () => {|LC010:db.SaveChanges()|};
            Action runSaves = () =>
            {
                foreach (var item in items)
                {
                    saveCurrent();
                }
            };

            runSaves();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateCalledThroughTwoOuterDelegatesFromLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action saveCurrent = () => {|LC010:db.SaveChanges()|};
        Action runOnce = () => saveCurrent();
        Action runAll = () => runOnce();

        foreach (var item in items)
        {
            runAll();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateAliasCalledInsideLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action saveCurrent = () => {|LC010:db.SaveChanges()|};
        Action alias = saveCurrent;

        foreach (var item in items)
        {
            alias();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateAliasAddedWithPlusEqualsInsideLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action saveCurrent = () => {|LC010:db.SaveChanges()|};
        Action alias = () => { };
        alias += saveCurrent;

        foreach (var item in items)
        {
            alias();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateRemovedBeforeAlias_ShouldNotTrigger()
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
            db.SaveChanges();
        }

        Action saveCurrent = SaveCurrent;
        saveCurrent -= SaveCurrent;
        Action alias = saveCurrent;

        foreach (var item in items)
        {
            alias?.Invoke();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateCalledThroughTwoOuterDelegatesBeforeLaterReassignment_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action saveCurrent = () => {|LC010:db.SaveChanges()|};
        Action runOnce = () => saveCurrent();
        Action runAll = () => runOnce();

        foreach (var item in items)
        {
            runAll();
        }

        saveCurrent = () => { };
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateCalledThroughOuterDelegatesAfterReassignmentBeforeLoop_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action saveCurrent = () => db.SaveChanges();
        Action runOnce = () => saveCurrent();
        Action runAll = () => runOnce();
        saveCurrent = () => { };

        foreach (var item in items)
        {
            runAll();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ConditionalLocalFunctionDelegateCalledInsideLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main(bool condition)
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        void SaveCurrent()
        {
            {|LC010:db.SaveChanges()|};
        }

        void Noop()
        {
        }

        Action saveCurrent = condition ? SaveCurrent : Noop;

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateDeclaredInsideOuterDelegateCalledFromLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action runSaves = () =>
        {
            Action saveCurrent = () => {|LC010:db.SaveChanges()|};
            saveCurrent();
        };

        foreach (var item in items)
        {
            runSaves();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ConditionalDelegateReassignmentBeforeLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main(bool condition)
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action saveCurrent = () => {|LC010:db.SaveChanges()|};
        if (condition)
            saveCurrent = () => { };

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateAssignedInsideCalledLocalFunctionBeforeLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        void Configure()
        {
            saveCurrent = () => {|LC010:db.SaveChanges()|};
        }

        Configure();

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateAssignedInsideConditionallyCalledLocalFunctionBeforeLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main(bool condition)
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        void Configure()
        {
            saveCurrent = () => {|LC010:db.SaveChanges()|};
        }

        if (condition)
        {
            Configure();
        }

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateAssignedInCalledLocalFunctionWithConditionalLaterOverwrite_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main(bool condition)
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        void Configure()
        {
            saveCurrent = () => {|LC010:db.SaveChanges()|};
            if (condition)
            {
                saveCurrent = () => { };
            }
        }

        Configure();

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateAssignedInCalledLocalFunctionWithUnconditionalLaterOverwrite_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        void Configure()
        {
            saveCurrent = () => db.SaveChanges();
            saveCurrent = () => { };
        }

        Configure();

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateAssignedInsideCalledLocalFunctionBeforeOuterRetryLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        void Configure()
        {
            saveCurrent = () => {|LC010:db.SaveChanges()|};
        }

        Configure();

        foreach (var item in items)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    saveCurrent();
                    break;
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateAssignedInNestedCalledSetupHelperBeforeLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        void Configure()
        {
            saveCurrent = () => {|LC010:db.SaveChanges()|};
        }

        void RunSaves()
        {
            Configure();

            foreach (var item in items)
            {
                saveCurrent();
            }
        }

        RunSaves();
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateAssignedInNestedSetupHelperBeforeRootLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        void Configure()
        {
            saveCurrent = () => {|LC010:db.SaveChanges()|};
        }

        void RunSetup()
        {
            Configure();
        }

        RunSetup();

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateReassignmentInsideUncalledLocalFunctionBeforeLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action saveCurrent = () => {|LC010:db.SaveChanges()|};

        void Configure()
        {
            saveCurrent = () => { };
        }

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateAssignedInIfBranchButCalledInElseLoop_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main(bool condition)
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        if (condition)
        {
            saveCurrent = () => db.SaveChanges();
        }
        else
        {
            foreach (var item in items)
            {
                saveCurrent();
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateAssignedInReturnBranchBeforeLoop_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main(bool condition)
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        if (condition)
        {
            saveCurrent = () => db.SaveChanges();
            return;
        }

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateAssignedInLocalFunctionReturnBranchBeforeLoop_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main(bool condition)
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        void RunSaves()
        {
            Action saveCurrent = () => { };
            if (condition)
            {
                saveCurrent = () => db.SaveChanges();
                return;
            }

            foreach (var item in items)
            {
                saveCurrent();
            }
        }

        RunSaves();
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateReassignedInIfBranchButCalledInElseLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main(bool condition)
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action saveCurrent = () => {|LC010:db.SaveChanges()|};

        if (condition)
        {
            saveCurrent = () => { };
        }
        else
        {
            foreach (var item in items)
            {
                saveCurrent();
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_WrapperDelegateAssignedInIfBranchButCalledInElseLoop_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main(bool condition)
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => db.SaveChanges();
        Action runSaves = () => { };

        if (condition)
        {
            runSaves = () => saveCurrent();
        }
        else
        {
            foreach (var item in items)
            {
                runSaves();
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_SaveDelegateAssignedInIfBranchLocalFunctionCalledInElseLoop_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main(bool condition)
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        void RunSaves()
        {
            saveCurrent();
        }

        if (condition)
        {
            saveCurrent = () => db.SaveChanges();
        }
        else
        {
            foreach (var item in items)
            {
                RunSaves();
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateAssignedBeforeContinueThenCalledAfterBranch_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main(bool skip)
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        foreach (var item in items)
        {
            if (skip)
            {
                saveCurrent = () => db.SaveChanges();
                continue;
            }

            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateAssignedBeforeContinueWithLoopVariantGuardThenCalledAfterBranch_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 0, 2 };
        Action saveCurrent = () => { };

        foreach (var item in items)
        {
            if (item > 0)
            {
                saveCurrent = () => {|LC010:db.SaveChanges()|};
                continue;
            }

            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateAssignedInOppositeLoopVariantBranchThenCalledLater_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 0, 2 };
        Action saveCurrent = () => { };

        foreach (var item in items)
        {
            if (item > 0)
            {
                saveCurrent();
            }
            else
            {
                saveCurrent = () => {|LC010:db.SaveChanges()|};
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateAssignedInOppositeStableBranchThenCalledLater_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main(bool saveNow)
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        foreach (var item in items)
        {
            if (saveNow)
            {
                saveCurrent();
            }
            else
            {
                saveCurrent = () => db.SaveChanges();
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateAssignedInConditionCalledWhenConditionEqualsFalse_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main(bool saveNow)
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        if (saveNow)
        {
            saveCurrent = () => db.SaveChanges();
        }

        if (saveNow == false)
        {
            foreach (var item in items)
            {
                saveCurrent();
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ConditionalDelegateInitializerCalledInsideLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main(bool condition)
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = condition ? () => {|LC010:db.SaveChanges()|} : () => { };

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_ConditionalDelegateInitializerCalledOnlyInOppositeBranch_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main(bool condition)
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = condition ? () => db.SaveChanges() : () => { };

        if (!condition)
        {
            foreach (var item in items)
            {
                saveCurrent();
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_BranchGuardedDelegateAssignmentGuardReassignedBeforeOppositeBranch_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main(bool condition)
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        if (condition)
        {
            saveCurrent = () => {|LC010:db.SaveChanges()|};
        }

        condition = false;
        if (!condition)
        {
            foreach (var item in items)
            {
                saveCurrent();
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ConditionalDelegateInitializerGuardReassignedBeforeOppositeBranch_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        var shouldSave = true;
        Action saveCurrent = shouldSave ? () => {|LC010:db.SaveChanges()|} : () => { };

        shouldSave = false;
        if (!shouldSave)
        {
            foreach (var item in items)
            {
                saveCurrent();
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateAssignedInIfButCalledInNegatedIf_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main(bool condition)
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        if (condition)
        {
            saveCurrent = () => db.SaveChanges();
        }

        if (!condition)
        {
            foreach (var item in items)
            {
                saveCurrent();
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateRetryLoopInsideOuterLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => {|LC010:db.SaveChanges()|};

        foreach (var item in items)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    saveCurrent();
                    break;
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateCalledInsideUncalledLocalFunctionLoop_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action saveCurrent = () => db.SaveChanges();

        void NotCalled()
        {
            foreach (var item in items)
            {
                saveCurrent();
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateReassignedInsideCalledLocalFunctionBeforeLoop_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action saveCurrent = () => db.SaveChanges();

        void RunSaves()
        {
            saveCurrent = () => { };

            foreach (var item in items)
            {
                saveCurrent();
            }
        }

        RunSaves();
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateReassignedByCalledHelperBeforeLoop_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action saveCurrent = () => db.SaveChanges();

        void DisableSave()
        {
            saveCurrent = () => { };
        }

        DisableSave();

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateReassignedByNestedHelperBeforeLoop_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => db.SaveChanges();

        void RunSaves()
        {
            void DisableSave()
            {
                saveCurrent = () => { };
            }

            DisableSave();

            foreach (var item in items)
            {
                saveCurrent();
            }
        }

        RunSaves();
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateRetryLoopWithBreakAfterSuccess_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        Action saveCurrent = () => db.SaveChanges();

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                saveCurrent();
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
    public async Task TestInnocent_DelegateAssignedViaOutBeforeLoopCall_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action saveCurrent = () => db.SaveChanges();
        Configure(out saveCurrent);

        foreach (var item in items)
        {
            saveCurrent();
        }
    }

    void Configure(out Action saveCurrent)
    {
        saveCurrent = () => { };
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_MethodGroupDelegateRetryLoopWithBreakAfterSuccess_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        Func<int> saveCurrent = db.SaveChanges;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                saveCurrent();
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
    public async Task TestInnocent_DelegateLoopInsideUncalledNestedHelper_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => db.SaveChanges();

        void RunSaves()
        {
            foreach (var item in items)
            {
                saveCurrent();
            }
        }

        void Outer()
        {
            RunSaves();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateLoopInsideUncalledLambda_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => db.SaveChanges();

        Action runSaves = () =>
        {
            foreach (var item in items)
            {
                saveCurrent();
            }
        };
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegatePassedToWrapperInitializer_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action saveCurrent = Wrap(() => db.SaveChanges());

        foreach (var item in items)
        {
            saveCurrent();
        }
    }

    Action Wrap(Action callback)
    {
        return () => { };
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateNullConditionalNonInvokeInsideLoop_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => db.SaveChanges();

        foreach (var item in items)
        {
            _ = saveCurrent?.ToString();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_LocalFunctionDelegateAssignedInsideCalledSetupHelper_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        void SaveCurrent()
        {
            {|LC010:db.SaveChanges()|};
        }

        void Configure()
        {
            saveCurrent = SaveCurrent;
        }

        Configure();

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateAssignmentInsideUncalledLocalFunction_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        void SaveCurrent()
        {
            db.SaveChanges();
        }

        void Configure()
        {
            saveCurrent = SaveCurrent;
        }

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_SaveChangesInsideNestedRetryLoopWithReturnAfterSuccess_ShouldNotTrigger()
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
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    db.SaveChanges();
                    return;
                }
                catch (Exception)
                {
                    if (attempt == 2)
                        throw;
                }
            }
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
    public async Task TestCrime_SaveChangesInsideRetryLoopNestedInOuterLoop_ShouldTriggerLC010()
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
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    {|LC010:db.SaveChanges()|};
                    break;
                }
                catch (Exception)
                {
                    if (attempt == 2)
                        throw;
                }
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
    public async Task TestCrime_SaveChangesInsideSwitchRetryBreakNestedInLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main(int state)
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        foreach (var item in items)
        {
            switch (state)
            {
                case 0:
                    try
                    {
                        {|LC010:db.SaveChanges()|};
                        break;
                    }
                    catch (Exception)
                    {
                    }

                    break;
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
    public async Task TestInnocent_LambdaAssignedToDelegateCalledOutsideLoop_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action saveCurrent = () => db.SaveChanges();

        foreach (var item in items)
        {
        }

        saveCurrent();
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateReassignedBeforeLoopCall_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        Action saveCurrent = () => db.SaveChanges();
        saveCurrent = () => { };

        foreach (var item in items)
        {
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateCapturesFreshContextDeclaredInsideLoop_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var items = new List<int> { 1, 2, 3 };

        foreach (var item in items)
        {
            using var db = new MyDbContext();
            Action saveCurrent = () => db.SaveChanges();
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_ConditionalFreshContextDelegateCarriedToLaterIteration_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        foreach (var item in items)
        {
            using var db = new MyDbContext();

            if (item == 1)
            {
                saveCurrent = () => {|LC010:db.SaveChanges()|};
            }

            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateParameterReceivesFreshContextDeclaredInsideLoop_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var items = new List<int> { 1, 2, 3 };
        Action<MyDbContext> saveCurrent = db => db.SaveChanges();

        foreach (var item in items)
        {
            using var db = new MyDbContext();
            saveCurrent(db);
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_FreshContextDelegateParameterReassignedOnlyBeforeReturn_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main(bool skip)
    {
        using var shared = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action<MyDbContext> saveCurrent = db =>
        {
            if (skip)
            {
                db = shared;
                return;
            }

            db.SaveChanges();
        };

        foreach (var item in items)
        {
            using var db = new MyDbContext();
            saveCurrent(db);
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_FreshContextPassedThroughOuterDelegate_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var items = new List<int> { 1, 2, 3 };
        Action<MyDbContext> saveCurrent = db => db.SaveChanges();
        Action<MyDbContext> runSave = db => saveCurrent(db);

        foreach (var item in items)
        {
            using var db = new MyDbContext();
            runSave(db);
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateCapturesFreshContextReassignedAfterSave_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var shared = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        foreach (var item in items)
        {
            var db = new MyDbContext();
            Action saveCurrent = () =>
            {
                db.SaveChanges();
                db = shared;
            };

            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateCapturesFreshContextWithUnusedNestedReassignment_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var shared = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        foreach (var item in items)
        {
            var db = new MyDbContext();
            Action saveCurrent = () => db.SaveChanges();
            Action unused = () => { db = shared; };
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateCapturesFreshContextWithUnusedNestedReassignmentBeforeSave_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var shared = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        foreach (var item in items)
        {
            var db = new MyDbContext();
            Action saveCurrent = () =>
            {
                Action unused = () => { db = shared; };
                db.SaveChanges();
            };

            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateCapturesFreshContextWithCalledHelperReassignmentBeforeSave_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var shared = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        foreach (var item in items)
        {
            var db = new MyDbContext();

            void UseShared()
            {
                db = shared;
            }

            Action saveCurrent = () =>
            {
                UseShared();
                {|LC010:db.SaveChanges()|};
            };

            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_MethodGroupDelegateCapturesFreshContextDeclaredInsideLoop_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var items = new List<int> { 1, 2, 3 };

        foreach (var item in items)
        {
            using var db = new MyDbContext();
            Func<int> saveCurrent = db.SaveChanges;
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_MethodGroupDelegateCapturesFreshContextReassignedAfterCapture_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var shared = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        foreach (var item in items)
        {
            var db = new MyDbContext();
            Func<int> saveCurrent = db.SaveChanges;
            db = shared;
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateCapturesLoopLocalContextReassignedBeforeInvocation_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var shared = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };

        foreach (var item in items)
        {
            var db = new MyDbContext();
            Action saveCurrent = () => {|LC010:db.SaveChanges()|};
            db = shared;
            saveCurrent();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateParameterReassignedBeforeSave_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var shared = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action<MyDbContext> saveCurrent = db =>
        {
            db = shared;
            {|LC010:db.SaveChanges()|};
        };

        foreach (var item in items)
        {
            using var db = new MyDbContext();
            saveCurrent(db);
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_WrapperDelegateParameterReassignedBeforeInnerDelegate_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var shared = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action<MyDbContext> saveCurrent = db => {|LC010:db.SaveChanges()|};
        Action<MyDbContext> runSave = db =>
        {
            db = shared;
            saveCurrent(db);
        };

        foreach (var item in items)
        {
            using var db = new MyDbContext();
            runSave(db);
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateAssignedAfterLoopInvocation_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        foreach (var item in items)
        {
            saveCurrent();
            saveCurrent = () => {|LC010:db.SaveChanges()|};
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateAssignedInsideInnerLoopBreakAfterOuterLoopInvocation_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        foreach (var item in items)
        {
            saveCurrent();
            while (true)
            {
                saveCurrent = () => {|LC010:db.SaveChanges()|};
                break;
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateAssignedAfterLocalInvokerCallbackInsideLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        void Invoke(Action callback)
        {
            callback();
        }

        foreach (var item in items)
        {
            Invoke(saveCurrent);
            saveCurrent = () => {|LC010:db.SaveChanges()|};
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateAssignedAfterLocalInvokerCallbackThenOverwritten_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        void Invoke(Action callback)
        {
            callback();
        }

        foreach (var item in items)
        {
            Invoke(saveCurrent);
            saveCurrent = () => db.SaveChanges();
            saveCurrent = () => { };
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateAssignedAfterInvocationInsideLocalFunctionCalledFromLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        void RunSave()
        {
            saveCurrent();
            saveCurrent = () => {|LC010:db.SaveChanges()|};
        }

        foreach (var item in items)
        {
            RunSave();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_DelegateAssignedAfterInvocationInsideOuterDelegateCalledFromLoop_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        Action runSave = () =>
        {
            saveCurrent();
            saveCurrent = () => {|LC010:db.SaveChanges()|};
        };

        foreach (var item in items)
        {
            runSave();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateAssignedAfterInvocationInsideLocalFunctionThenOverwritten_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        void RunSave()
        {
            saveCurrent();
            saveCurrent = () => db.SaveChanges();
            saveCurrent = () => { };
        }

        foreach (var item in items)
        {
            RunSave();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateAssignedAfterInvocationInsideOuterDelegateThenOverwritten_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        Action runSave = () =>
        {
            saveCurrent();
            saveCurrent = () => db.SaveChanges();
            saveCurrent = () => { };
        };

        foreach (var item in items)
        {
            runSave();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_LoopCarriedFreshContextDelegateAssignedAfterInvocation_ShouldTriggerLC010()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        foreach (var item in items)
        {
            saveCurrent();
            using var db = new MyDbContext();
            saveCurrent = () => {|LC010:db.SaveChanges()|};
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateAssignedAfterLoopInvocationThenBreak_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        foreach (var item in items)
        {
            saveCurrent();
            saveCurrent = () => db.SaveChanges();
            break;
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DelegateAssignedAfterLoopInvocationThenOverwritten_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        using var db = new MyDbContext();
        var items = new List<int> { 1, 2, 3 };
        Action saveCurrent = () => { };

        foreach (var item in items)
        {
            saveCurrent();
            saveCurrent = () => db.SaveChanges();
            saveCurrent = () => { };
        }
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
