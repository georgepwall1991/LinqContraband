using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC046_ConcurrentDbContextOperations.ConcurrentDbContextOperationsAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC046_ConcurrentDbContextOperations;

public sealed class ConcurrentDbContextOperationsFlowTests
{
    private const string EfMock = ConcurrentDbContextOperationsTests.EfMock;

    [Fact]
    public async Task ContextAlias_WithSameOrigin_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var alias = db;
            await Task.WhenAll(
                {|#0:db.Users.ToListAsync()|},
                {|#1:alias.Users.AnyAsync()|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task QueryAlias_WithSameOrigin_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public bool Active { get; set; } }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var query = db.Users.Where(user => user.Active);
            await Task.WhenAll(
                {|#0:query.ToListAsync()|},
                {|#1:query.AnyAsync()|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task FirstOperationInOptionalBranch_ThenSecondAfterJoin_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool includeFirst)
        {
            if (includeFirst)
            {
                _ = db.Users.ToListAsync();
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOperationInOnlyReachingBranch_ThenSecondAfterJoin_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool start)
        {
            Task first;
            if (start)
                first = {|#0:db.Users.ToListAsync()|};
            else
                return;

            await {|#1:db.Users.AnyAsync()|};
            await first;
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task OperationsInMutuallyExclusiveBranches_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool firstBranch)
        {
            Task task;
            if (firstBranch)
                task = db.Users.ToListAsync();
            else
                task = db.Users.AnyAsync();

            await task;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ActiveOperationBeforeOptionalSecond_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool includeSecond)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            if (includeSecond)
                await {|#1:db.Users.AnyAsync()|};
            await first;
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ActiveOperationConditionallyAwaitedBeforeSecond_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool awaitFirst)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            if (awaitFirst)
                await first;

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StoredWhenAllArray_ConditionallyAwaitedBeforeSecond_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool awaitFirst)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] inputs = { first };
            if (awaitFirst)
                await Task.WhenAll(inputs);

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StoredWhenAllArray_ConditionallyThenUnconditionallyAwaited_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool awaitEarly)
        {
            var first = db.Users.ToListAsync();
            Task[] inputs = { first };
            if (awaitEarly)
                await Task.WhenAll(inputs);

            await Task.WhenAll(inputs);
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StoredWhenAllArray_AwaitedInBothBranches_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool useFirstPath)
        {
            var first = db.Users.ToListAsync();
            Task[] inputs = { first };
            if (useFirstPath)
                await Task.WhenAll(inputs);
            else
                await Task.WhenAll(inputs);

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StoredWhenAllArray_AndDirectAwaitInComplementaryBranches_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool useAggregate)
        {
            var first = db.Users.ToListAsync();
            Task[] inputs = { first };
            if (useAggregate)
                await Task.WhenAll(inputs);
            else
                await first;

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DirectAwait_AndStoredWhenAllArrayInComplementaryBranches_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool useDirect)
        {
            var first = db.Users.ToListAsync();
            Task[] inputs = { first };
            if (useDirect)
                await first;
            else
                await Task.WhenAll(inputs);

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StoredWhenAllArray_ElementReplacedBeforeAwait_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] inputs = { first };
            inputs[0] = Task.CompletedTask;

            await Task.WhenAll(inputs);
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StoredWhenAllArray_AliasElementReplacedBeforeAwait_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] inputs = { first };
            var alias = inputs;
            alias[0] = Task.CompletedTask;

            await Task.WhenAll(inputs);
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StoredWhenAllArray_ElementReplacedByLocalFunction_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] inputs = { first };
            void Replace() => inputs[0] = Task.CompletedTask;
            Replace();

            await Task.WhenAll(inputs);
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StoredWhenAllArray_ElementReplacedByInvokedLambda_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] inputs = { first };
            ((Action)(() => inputs[0] = Task.CompletedTask))();

            await Task.WhenAll(inputs);
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StoredWhenAllArray_ElementReplacedAfterAwait_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            Task[] inputs = { first };
            await Task.WhenAll(inputs);

            inputs[0] = Task.CompletedTask;
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StoredWhenAllArray_ElseIfMaySkipCompletion_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool firstPath, bool secondPath)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] inputs = { first };
            if (firstPath)
                await Task.WhenAll(inputs);
            else if (secondPath)
                await Task.WhenAll(inputs);

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StoredWhenAllArray_LoopBranchMaySkipCompletion_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool firstPath, bool awaitInLoop)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] inputs = { first };
            if (firstPath)
                await Task.WhenAll(inputs);
            else
                while (awaitInLoop)
                {
                    await Task.WhenAll(inputs);
                    break;
                }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StoredWhenAllArray_AwaitedAfterSecond_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] inputs = { first };

            await {|#1:db.Users.AnyAsync()|};
            await Task.WhenAll(inputs);
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ActiveOperationAwaitedInBothBranchesBeforeSecond_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool useFirstPath)
        {
            var first = db.Users.ToListAsync();
            if (useFirstPath)
                await first;
            else
                await first;

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AwaitedWhenAny_MayLeaveEfTaskActive_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, int timeout)
        {
            await Task.WhenAny(
                {|#0:db.Users.ToListAsync()|},
                Task.Delay(timeout));
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StoredTaskAwaitedWhenAny_MayLeaveEfTaskActive_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, int timeout)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            await Task.WhenAny(first, Task.Delay(timeout));
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StoredTaskAwaitedSingleInputWhenAny_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            await Task.WhenAny(first);
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StableSingletonTaskArrayAwaitedWhenAny_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            await Task.WhenAny(tasks);
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SeparatelyAssignedSingletonTaskArrayAwaitedWhenAny_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks;
            tasks = new Task[] { first };
            await Task.WhenAny(tasks);
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SingletonTaskArrayAllocationInContinuingTry_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            try
            {
                Task[] tasks = { first };
                await Task.WhenAny(tasks);
            }
            catch (OutOfMemoryException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task SingletonTaskArrayCompletionInNestedBlockWithContinuingCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            {
                try
                {
                    Task[] tasks = { first };
                    await Task.WhenAny(tasks);
                }
                catch (OutOfMemoryException)
                {
                }
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task OppositeBranchTryCatchCanFallThrough_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void MayThrow()
        {
            throw new InvalidOperationException();
        }

        public async Task Run(AppDbContext db, bool condition)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            if (condition)
            {
                await first;
            }
            else
            {
                try
                {
                    MayThrow();
                    return;
                }
                catch (InvalidOperationException)
                {
                }
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task SingletonTaskArrayAllocationWithMismatchedCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                Task[] tasks = { first };
                await Task.WhenAny(tasks);
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FixedSizeArrayWithMismatchedCatchBeforeAwait_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                var buffer = new byte[1];
                await first;
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OptionalWhenAllBeforeSingletonArrayWhenAny_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool condition)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            if (condition)
            {
                await Task.WhenAll(tasks);
            }

            await Task.WhenAny(tasks);
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OptionalWhenAnyBeforeArrayWhenAll_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool condition, int timeout)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first, Task.Delay(timeout) };
            if (condition)
            {
                await Task.WhenAny(tasks);
            }

            await Task.WhenAll(tasks);
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StableSingletonTaskArrayCompletionWithOppositeBranchEscape_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void Observe(Task[] tasks)
        {
        }

        public async Task Run(AppDbContext db, bool condition)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            if (condition)
            {
                await Task.WhenAll(tasks);
                await db.Users.AnyAsync();
            }
            else
            {
                Observe(tasks);
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task WrappedEfTaskInSingletonTaskArrayAwaitedWhenAny_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { Task.FromResult(first) };
            await Task.WhenAny(tasks);
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StoredWrappedEfTaskInSingletonTaskArrayAwaitedWhenAny_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            var wrapped = Task.FromResult(first);
            Task[] tasks = { wrapped };
            await Task.WhenAny(tasks);
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task SeparatelyAssignedWrappedEfTaskInSingletonTaskArray_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task<Task<List<User>>> wrapped;
            wrapped = Task.FromResult(first);
            Task[] tasks = { wrapped };
            await Task.WhenAny(tasks);
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task UnknownTaskReturningConsumerInTaskArray_ShouldEscapeWithoutTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static Task DrainAndReturnTask(Task task)
        {
            task.GetAwaiter().GetResult();
            return Task.CompletedTask;
        }

        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { DrainAndReturnTask(first) };
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UnknownConsumerThroughTaskFromResult_ShouldEscapeWithoutTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void Drain<T>(Task<Task<T>> task)
        {
            task.Unwrap().GetAwaiter().GetResult();
        }

        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            Drain(Task.FromResult(first));
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StoredTaskFromResultPassedToUnknownConsumer_ShouldEscapeWithoutTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void Drain<T>(Task<Task<T>> task)
        {
            task.Unwrap().GetAwaiter().GetResult();
        }

        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            var wrapped = Task.FromResult(first);
            Drain(wrapped);
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StoredTaskFromResultConsumedAfterOverlap_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void Drain(Task<Task> task)
        {
            task.Unwrap().GetAwaiter().GetResult();
        }

        public async Task Run(AppDbContext db)
        {
            Task first = {|#0:db.Users.ToListAsync()|};
            var wrapped = Task.FromResult(first);
            await {|#1:db.Users.AnyAsync()|};
            Drain(wrapped);
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StoredTaskFromResultConsumedOnOppositeBranch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void Drain(Task<Task> task)
        {
            task.Unwrap().GetAwaiter().GetResult();
        }

        public async Task Run(AppDbContext db, bool condition)
        {
            Task first = {|#0:db.Users.ToListAsync()|};
            var wrapped = Task.FromResult(first);
            if (condition)
                Drain(wrapped);
            else
                await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StoredTaskFromResultConsumedOnEveryBranch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void Drain<T>(Task<Task<T>> task)
        {
            task.Unwrap().GetAwaiter().GetResult();
        }

        public async Task Run(AppDbContext db, bool condition)
        {
            var first = db.Users.ToListAsync();
            var wrapped = Task.FromResult(first);
            if (condition)
                Drain(wrapped);
            else
                Drain(wrapped);

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StoredTaskFromResultEscapeAndAwaitOnEveryBranch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void Drain<T>(Task<Task<T>> task)
        {
            task.Unwrap().GetAwaiter().GetResult();
        }

        public async Task Run(AppDbContext db, bool condition)
        {
            var first = db.Users.ToListAsync();
            var wrapped = Task.FromResult(first);
            if (condition)
                Drain(wrapped);
            else
                await first;

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskFromResultCanFailBeforeUnknownConsumer_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void Drain<T>(Task<Task<T>> task)
        {
            task.Unwrap().GetAwaiter().GetResult();
        }

        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            try
            {
                Drain(Task.FromResult(first));
            }
            catch (OutOfMemoryException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ReassignedStoredTaskFromResultConsumedLater_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void Drain(Task<Task> task)
        {
            task.Unwrap().GetAwaiter().GetResult();
        }

        public async Task Run(AppDbContext db)
        {
            Task first = {|#0:db.Users.ToListAsync()|};
            var wrapped = Task.FromResult(first);
            wrapped = Task.FromResult(Task.CompletedTask);
            Drain(wrapped);
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StableSingletonTaskArrayAwaitedWhenAnyWithMismatchedCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            try
            {
                await Task.WhenAny(tasks);
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StableSingletonTaskArrayWhenAnyAllocationCanReachCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                await Task.WhenAny(tasks);
            }
            catch (OutOfMemoryException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StoredStableSingletonTaskArrayWhenAnyAllocationCanReachCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                var any = Task.WhenAny(tasks);
                await any;
            }
            catch (OutOfMemoryException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StableSingletonTaskArrayNullableLengthConversionCanReachCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, int? length)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                var buffer = new byte[(int)length];
                await Task.WhenAny(tasks);
            }
            catch (InvalidOperationException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StableSingletonTaskArrayUserDefinedLengthConversionCanReachCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class ArrayLength
    {
        public static implicit operator int(ArrayLength value)
        {
            throw new InvalidOperationException();
        }
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, ArrayLength length)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                var buffer = new byte[length];
                await Task.WhenAny(tasks);
            }
            catch (InvalidOperationException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StableSingletonTaskArrayCaughtOppositeBranchThrow_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool completeFirst)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                if (completeFirst)
                {
                    await Task.WhenAny(tasks);
                }
                else
                {
                    throw new InvalidOperationException();
                }
            }
            catch (InvalidOperationException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StableSingletonTaskArrayCaughtOppositeBranchPrefixThrow_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void ThrowingHelper()
        {
            throw new InvalidOperationException();
        }

        public async Task Run(AppDbContext db, bool completeFirst)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                if (completeFirst)
                {
                    await Task.WhenAny(tasks);
                }
                else
                {
                    ThrowingHelper();
                    return;
                }
            }
            catch (InvalidOperationException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StableSingletonTaskArrayCaughtOppositeBranchReturnExpressionFailure_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static int ThrowingValue()
        {
            throw new InvalidOperationException();
        }

        public async Task<int> Run(AppDbContext db, bool completeFirst)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                if (completeFirst)
                {
                    await Task.WhenAny(tasks);
                }
                else
                {
                    return ThrowingValue();
                }
            }
            catch (InvalidOperationException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
            return 0;
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StableSingletonTaskArrayCaughtThrowOperandFailure_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static InvalidOperationException Create()
        {
            throw new ArgumentException();
        }

        public async Task Run(AppDbContext db, bool completeFirst)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                if (completeFirst)
                {
                    await Task.WhenAny(tasks);
                }
                else
                {
                    throw Create();
                }
            }
            catch (ArgumentException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StableSingletonTaskArrayCaughtNestedReturnPrefixThrow_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void Fail()
        {
            throw new InvalidOperationException();
        }

        public async Task Run(AppDbContext db, bool completeFirst)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                if (completeFirst)
                {
                    await Task.WhenAny(tasks);
                }
                else
                {
                    try
                    {
                        Fail();
                        return;
                    }
                    finally
                    {
                    }
                }
            }
            catch (InvalidOperationException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StoredStableSingletonTaskArrayWhenAnyAwaitedLater_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            var any = Task.WhenAny(tasks);
            await any;
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StoredSingletonTaskArrayWhenAnyNotAwaitedBeforeSecond_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            var any = Task.WhenAny(tasks);
            await {|#1:db.Users.AnyAsync()|};
            await any;
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ReassignedStoredSingletonTaskArrayWhenAny_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            var any = Task.WhenAny(tasks);
            any = Task.FromResult(Task.CompletedTask);
            await any;
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StableSingletonTaskArrayWhenAnyConfigureAwaitWithMismatchedCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            try
            {
                await Task.WhenAny(tasks).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UserDefinedAsTaskAroundSingletonWhenAny_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Custom;" + EfMock + @"
namespace Custom
{
    public static class TaskExtensions
    {
        public static Task AsTask(this Task<Task> task) => Task.CompletedTask;
    }
}

namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            await Task.WhenAny(tasks).AsTask();
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TaskConsumerInArrayBound_ShouldEscapeWithoutTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static int Drain(Task task)
        {
            task.GetAwaiter().GetResult();
            return 1;
        }

        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            var buffer = new byte[Drain(first)];
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskConsumerInUnrelatedArrayInitializer_ShouldEscapeWithoutTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static int Drain(Task task)
        {
            task.GetAwaiter().GetResult();
            return 1;
        }

        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            int[] values = { Drain(first) };
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskConsumerInTaskArrayIndex_ShouldEscapeWithoutTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static int Drain(Task task)
        {
            task.GetAwaiter().GetResult();
            return 0;
        }

        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            Task[] source = { Task.CompletedTask };
            Task[] tasks = { source[Drain(first)] };
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FixedSizeArrayAllocationCanBypassSingletonWhenAny_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                var buffer = new byte[1];
                await Task.WhenAny(tasks);
            }
            catch (OutOfMemoryException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task RuntimeArrayLengthOverflowCanBypassSingletonWhenAny_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, int length)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                var buffer = new byte[length];
                await Task.WhenAny(tasks);
            }
            catch (OverflowException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task OversizedConstantArrayLengthCanBypassSingletonWhenAny_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                var buffer = new byte[long.MaxValue];
                await Task.WhenAny(tasks);
            }
            catch (OverflowException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task EarlierCatchInterceptingArrayAllocation_ShouldNotReachLaterCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                var buffer = new byte[1];
                await first;
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedArrayAllocationCatchRethrowCanReachContinuingOuterCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            try
            {
                try
                {
                    Task[] tasks = { first };
                    await Task.WhenAny(tasks);
                }
                catch (OutOfMemoryException)
                {
                    throw;
                }
            }
            catch (OutOfMemoryException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NestedArrayAllocationCatchReplacementCanReachTypedOuterCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            try
            {
                try
                {
                    Task[] tasks = { first };
                    await Task.WhenAny(tasks);
                }
                catch (OutOfMemoryException)
                {
                    throw new InvalidOperationException();
                }
            }
            catch (InvalidOperationException)
            {
                await {|#1:db.Users.AnyAsync()|};
            }
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NestedArrayAllocationCatchWithUnreachableRethrow_ShouldNotReachOuterCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                try
                {
                    Task[] tasks = { first };
                    await Task.WhenAll(tasks);
                }
                catch (OutOfMemoryException)
                {
                    if (false)
                    {
                        throw;
                    }
                }
            }
            catch (OutOfMemoryException)
            {
                await db.Users.AnyAsync();
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedDifferentCatchBareRethrow_ShouldNotReachOuterAllocationCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                try
                {
                    Task[] tasks = { first };
                    await Task.WhenAny(tasks);
                }
                catch (OutOfMemoryException)
                {
                    try
                    {
                        throw new InvalidOperationException();
                    }
                    catch (InvalidOperationException)
                    {
                        throw;
                    }
                }
            }
            catch (OutOfMemoryException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedCatchConsumesAllocationRethrow_ShouldNotReachOuterCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                try
                {
                    Task[] tasks = { first };
                    await Task.WhenAny(tasks);
                }
                catch (OutOfMemoryException)
                {
                    try
                    {
                        throw;
                    }
                    catch (OutOfMemoryException)
                    {
                    }

                    return;
                }
            }
            catch (OutOfMemoryException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExactBaseReplacement_ShouldNotReachNarrowOuterCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                try
                {
                    Task[] tasks = { first };
                    await Task.WhenAny(tasks);
                }
                catch (OutOfMemoryException)
                {
                    throw new Exception();
                }
            }
            catch (OutOfMemoryException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ParenthesizedExactBaseReplacement_ShouldNotReachNarrowOuterCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                try
                {
                    Task[] tasks = { first };
                    await Task.WhenAny(tasks);
                }
                catch (OutOfMemoryException)
                {
                    throw (new Exception());
                }
            }
            catch (OutOfMemoryException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StoredExactReplacement_ShouldNotReachNarrowOuterCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                try
                {
                    Task[] tasks = { first };
                    await Task.WhenAll(tasks);
                }
                catch (OutOfMemoryException)
                {
                    Exception replacement = new InvalidOperationException();
                    throw replacement;
                }
            }
            catch (OutOfMemoryException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ThrowingCustomReplacementConstructor_ShouldReachOuterContinuingCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class ReplacementException : Exception
    {
        public ReplacementException()
        {
            throw new OutOfMemoryException();
        }
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            try
            {
                try
                {
                    Task[] tasks = { first };
                    await Task.WhenAny(tasks);
                }
                catch (OutOfMemoryException)
                {
                    Exception replacement = new ReplacementException();
                    throw replacement;
                }
            }
            catch (OutOfMemoryException)
            {
                await {|#1:db.Users.AnyAsync()|};
            }
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ConditionallyAssignedTaskFromResultEscape_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void Drain(Task<Task> task)
        {
            task.Unwrap().GetAwaiter().GetResult();
        }

        public async Task Run(AppDbContext db, bool condition)
        {
            Task first = {|#0:db.Users.ToListAsync()|};
            Task<Task> wrapped = Task.FromResult(Task.CompletedTask);
            if (condition)
            {
                wrapped = Task.FromResult(first);
            }

            Drain(wrapped);
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task UserDefinedTaskSequenceConversion_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class TaskSequence : IEnumerable<Task>
    {
        public static implicit operator TaskSequence(Task[] tasks)
        {
            return new TaskSequence();
        }

        public IEnumerator<Task> GetEnumerator()
        {
            yield return Task.CompletedTask;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            await Task.WhenAny((TaskSequence)tasks);
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ExactExceptionRethrownThroughBroadCatch_ShouldNotReachNarrowOuterCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                try
                {
                    Task[] tasks = { first };
                    await Task.WhenAny(tasks);
                }
                catch (OutOfMemoryException)
                {
                    try
                    {
                        throw new InvalidOperationException();
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                }
            }
            catch (OutOfMemoryException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ThrowOperandEvaluation_ShouldReachOuterContinuingCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static string ThrowDuringConstruction()
        {
            throw new OutOfMemoryException();
        }

        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            try
            {
                try
                {
                    Task[] tasks = { first };
                    await Task.WhenAny(tasks);
                }
                catch (OutOfMemoryException)
                {
                    try
                    {
                        throw new InvalidOperationException(
                            ThrowDuringConstruction());
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                }
            }
            catch (OutOfMemoryException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task EarlierMatchingCatchRethrow_ShouldReachOuterContinuingCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            try
            {
                try
                {
                    Task[] tasks = { first };
                    await Task.WhenAny(tasks);
                }
                catch (OutOfMemoryException)
                {
                    throw;
                }
                catch (Exception)
                {
                    return;
                }
            }
            catch (OutOfMemoryException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task EarlierUnknownFilteredCatchRethrow_ShouldReachOuterContinuingCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool condition)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            try
            {
                try
                {
                    Task[] tasks = { first };
                    await Task.WhenAny(tasks);
                }
                catch (OutOfMemoryException) when (condition)
                {
                    throw;
                }
                catch (OutOfMemoryException)
                {
                    return;
                }
            }
            catch (OutOfMemoryException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NestedFinallyReplacement_ShouldReachOuterContinuingCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            try
            {
                try
                {
                    Task[] tasks = { first };
                    await Task.WhenAny(tasks);
                }
                finally
                {
                    throw new InvalidOperationException();
                }
            }
            catch (InvalidOperationException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task UnreachableFinallyThrow_ShouldNotReachOuterCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                try
                {
                    Task[] tasks = { first };
                    await Task.WhenAll(tasks);
                }
                finally
                {
                    if (false)
                    {
                        throw new InvalidOperationException();
                    }
                }
            }
            catch (InvalidOperationException)
            {
                await db.Users.AnyAsync();
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AlwaysThrowingFinallyReplacement_ShouldNotReachMismatchedOuterCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                try
                {
                    Task[] tasks = { first };
                    await Task.WhenAll(tasks);
                }
                finally
                {
                    throw new InvalidOperationException();
                }
            }
            catch (OutOfMemoryException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ThrowingReturnInsideOppositeTryBranch_ShouldReachOuterContinuingCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static int ThrowingResult()
        {
            throw new InvalidOperationException();
        }

        public async Task<int> Run(AppDbContext db, bool condition)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            try
            {
                if (condition)
                {
                    await first;
                }
                else
                {
                    try
                    {
                        return ThrowingResult();
                    }
                    finally
                    {
                    }
                }
            }
            catch
            {
                await {|#1:db.Users.AnyAsync()|};
            }

            return 0;
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ThrowingFinallyInsideOppositeTryBranch_ShouldReachOuterContinuingCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool condition)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            try
            {
                if (condition)
                {
                    await first;
                }
                else
                {
                    try
                    {
                    }
                    finally
                    {
                        throw new InvalidOperationException();
                    }
                }
            }
            catch (InvalidOperationException)
            {
                await {|#1:db.Users.AnyAsync()|};
            }
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ReturningTryWithThrowingFinallyInsideOppositeBranch_ShouldReachOuterContinuingCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool condition)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            try
            {
                if (condition)
                {
                    await first;
                }
                else
                {
                    try
                    {
                        return;
                    }
                    finally
                    {
                        throw new InvalidOperationException();
                    }
                }
            }
            catch (InvalidOperationException)
            {
                await {|#1:db.Users.AnyAsync()|};
            }
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TerminalReturnInsideOppositeTryBranch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool condition)
        {
            var first = db.Users.ToListAsync();
            if (condition)
            {
                await first;
            }
            else
            {
                try
                {
                    return;
                }
                finally
                {
                }
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ThrowingHelperInTerminatingCatch_ShouldReachOuterContinuingCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void RethrowThroughHelper()
        {
            throw new OutOfMemoryException();
        }

        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            try
            {
                try
                {
                    Task[] tasks = { first };
                    await Task.WhenAny(tasks);
                }
                catch (OutOfMemoryException)
                {
                    RethrowThroughHelper();
                    return;
                }
            }
            catch (OutOfMemoryException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task FixedSizeArrayWithOverflowCatchBeforeSingletonWhenAny_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            try
            {
                var buffer = new byte[1];
                await Task.WhenAny(tasks);
            }
            catch (OverflowException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TwoElementTaskArrayAwaitedWhenAny_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, int timeout)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first, Task.Delay(timeout) };
            await Task.WhenAny(tasks);
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task MutatedSingletonTaskArrayAwaitedWhenAny_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, int timeout)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            tasks[0] = Task.Delay(timeout);
            await Task.WhenAny(tasks);
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task GotoMutationBeforeLexicallyEarlierWhenAny_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, int timeout)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            goto Mutate;

        AwaitTasks:
            await Task.WhenAny(tasks);
            await {|#1:db.Users.AnyAsync()|};
            return;

        Mutate:
            tasks[0] = Task.Delay(timeout);
            goto AwaitTasks;
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AliasedSingletonTaskArrayAwaitedWhenAny_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            var alias = tasks;
            await Task.WhenAny(alias);
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CapturedSingletonTaskArrayAwaitedWhenAny_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, int timeout)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            Action replace = () => tasks[0] = Task.Delay(timeout);
            replace();
            await Task.WhenAny(tasks);
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StoredTaskAwaitedSingleInputWhenAnyInTry_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                await Task.WhenAny(first);
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StoredSingleInputWhenAny_AwaitedBeforeSecondOperation_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var any = Task.WhenAny(db.Users.ToListAsync());
            await any;
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StoredWhenAll_AwaitedBeforeSecondOperation_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var all = Task.WhenAll(db.Users.ToListAsync());
            await all;
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StoredWhenAll_NotAwaitedBeforeSecondOperation_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var all = Task.WhenAll({|#0:db.Users.ToListAsync()|});
            await {|#1:db.Users.AnyAsync()|};
            await all;
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ThrowingStoredWhenAllArgument_WithContinuingCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static Task ThrowBeforeReturningTask() =>
            throw new InvalidOperationException();

        public async Task Run(AppDbContext db)
        {
            try
            {
                var all = Task.WhenAll(
                    {|#0:db.Users.ToListAsync()|},
                    ThrowBeforeReturningTask());
                await all;
            }
            catch (InvalidOperationException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TaskLocalWait_BeforeSecondOperation_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            first.Wait();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskLocalGetAwaiterGetResult_BeforeSecondOperation_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            first.GetAwaiter().GetResult();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ValueTaskAsTaskGetAwaiterGetResult_BeforeSecondOperation_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.FindAsync<User>(1);
            first.AsTask().GetAwaiter().GetResult();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ValueTaskGetAwaiterGetResult_BeforeSecondOperation_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.FindAsync<User>(1);
            first.GetAwaiter().GetResult();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ValueTaskAsTaskGetAwaiterGetResultInTry_BeforeSecondOperation_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.FindAsync<User>(1);
            try
            {
                first.AsTask().GetAwaiter().GetResult();
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskLocalTimedWait_BeforeSecondOperation_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, int timeout)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            first.Wait(timeout);
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ThrowBeforeTaskLocalWait_WithContinuingCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void MightThrow() => throw new InvalidOperationException();

        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            try
            {
                MightThrow();
                first.Wait();
            }
            catch (InvalidOperationException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TaskLocalConfigureAwaitInTry_WithContinuingCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                await first.ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskLocalWhenAllInTry_WithContinuingCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                await Task.WhenAll(first);
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RepeatedTaskLocalWhenAllInTry_WithContinuingCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                await Task.WhenAll(first, first);
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskLocalWhenAllWithCompletedTaskInTry_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                await Task.WhenAll(first, Task.CompletedTask);
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskLocalWhenAllWithConstructedTaskInTry_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var other = new Task(() => { });
            var first = db.Users.ToListAsync();
            try
            {
                await Task.WhenAll(first, other);
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskLocalWhenAllWithValueTaskAsTaskInTry_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            ValueTask other = default;
            var first = db.Users.ToListAsync();
            try
            {
                await Task.WhenAll(first, other.AsTask());
            }
            catch (ArgumentException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NullWhenAllInput_WithContinuingCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            try
            {
                await Task.WhenAll(
                    {|#0:db.Users.ToListAsync()|},
                    (Task)null);
            }
            catch (ArgumentException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NullWhenAllInput_WithMismatchedCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            try
            {
                await Task.WhenAll(
                    db.Users.ToListAsync(),
                    (Task)null);
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NullWhenAllElement_WithArgumentNullCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            try
            {
                await Task.WhenAll(
                    db.Users.ToListAsync(),
                    (Task)null);
            }
            catch (ArgumentNullException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NullWhenAllCollection_WithArgumentNullCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            try
            {
                await Task.WhenAll((Task[])null);
                await first;
            }
            catch (ArgumentNullException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NullWhenAllCollection_WithMismatchedCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                await Task.WhenAll((Task[])null);
                await first;
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConfiguredNullWhenAllCollection_WithMismatchedCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                await Task.WhenAll((Task[])null).ConfigureAwait(false);
                await first;
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StoredNullWhenAllElement_WithArgumentNullCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                Task[] inputs = { first, (Task)null };
                await Task.WhenAll(inputs);
            }
            catch (ArgumentNullException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DiscardedTaskLocal_ThenSameContextOperation_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            _ = first;
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TryStartAndCatchOperation_WithoutPostStartThrow_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void MightThrow() => throw new InvalidOperationException();

        public async Task Run(AppDbContext db)
        {
            try
            {
                MightThrow();
                _ = db.Users.ToListAsync();
            }
            catch (InvalidOperationException)
            {
                await db.Users.AnyAsync();
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TryStartAndCatchOperation_AfterPostStartThrow_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void MightThrow() => throw new InvalidOperationException();

        public async Task Run(AppDbContext db)
        {
            try
            {
                _ = {|#0:db.Users.ToListAsync()|};
                MightThrow();
            }
            catch (InvalidOperationException)
            {
                await {|#1:db.Users.AnyAsync()|};
            }
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TryStartAndCatchOperation_AfterBaseTypedPostStartThrow_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            Exception error = new InvalidOperationException();
            try
            {
                _ = {|#0:db.Users.ToListAsync()|};
                throw error;
            }
            catch (InvalidOperationException)
            {
                await {|#1:db.Users.AnyAsync()|};
            }
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TryStartAndMismatchedCatch_AfterStableBaseTypedThrow_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            Exception error = new ArgumentException();
            try
            {
                _ = db.Users.ToListAsync();
                throw error;
            }
            catch (InvalidOperationException)
            {
                await db.Users.AnyAsync();
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StableBaseTypedThrow_InterceptedByNarrowNestedCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            Exception error = new InvalidOperationException();
            try
            {
                _ = db.Users.ToListAsync();
                try
                {
                    throw error;
                }
                catch (InvalidOperationException)
                {
                }
            }
            catch (Exception)
            {
                await db.Users.AnyAsync();
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task BaseTypedThrow_NotDefinitelyInterceptedByNarrowNestedCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            Exception error = new ArgumentException();
            try
            {
                _ = {|#0:db.Users.ToListAsync()|};
                try
                {
                    throw error;
                }
                catch (InvalidOperationException)
                {
                }
            }
            catch (Exception)
            {
                await {|#1:db.Users.AnyAsync()|};
            }
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TryStartAndMismatchedCatchOperation_AfterPostStartThrow_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            try
            {
                _ = db.Users.ToListAsync();
                throw new ArgumentException();
            }
            catch (InvalidOperationException)
            {
                await db.Users.AnyAsync();
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TryStartAndFilteredOutCatchOperation_AfterPostStartThrow_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            try
            {
                _ = db.Users.ToListAsync();
                throw new InvalidOperationException();
            }
            catch (InvalidOperationException) when (false)
            {
                await db.Users.AnyAsync();
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TryStartAndOuterCatchOperation_WithNestedInterception_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            try
            {
                _ = db.Users.ToListAsync();
                try
                {
                    throw new InvalidOperationException();
                }
                catch (InvalidOperationException)
                {
                }
            }
            catch (InvalidOperationException)
            {
                await db.Users.AnyAsync();
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TwoSequentialOverlapGroups_ShouldReportEachGroup()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            await Task.WhenAll(
                {|#0:db.Users.ToListAsync()|},
                {|#1:db.Users.AnyAsync()|});
            await Task.WhenAll(
                {|#2:db.Users.ToListAsync()|},
                {|#3:db.Users.AnyAsync()|});
        }
    }
}";

        var firstGroup = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");
        var secondGroup = VerifyCS.Diagnostic()
            .WithLocation(3)
            .WithLocation(2)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, firstGroup, secondGroup);
    }

    [Fact]
    public async Task ThrowBeforeAwait_WithContinuingCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void MightThrow() => throw new InvalidOperationException();

        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            try
            {
                MightThrow();
                await first;
            }
            catch (InvalidOperationException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task MismatchedThrowBeforeAwait_WithContinuingCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                throw new ArgumentException();
                await first;
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FilteredOutThrowBeforeAwait_WithContinuingCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                throw new InvalidOperationException();
                await first;
            }
            catch (InvalidOperationException) when (false)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedInterceptedThrowBeforeAwait_WithContinuingCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                try
                {
                    throw new InvalidOperationException();
                }
                catch (InvalidOperationException)
                {
                }

                await first;
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ThrowingImmediateAwaitWrapperArgument_WithContinuingCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static bool GetFlag() => throw new InvalidOperationException();

        public async Task Run(AppDbContext db)
        {
            try
            {
                await {|#0:db.Users.ToListAsync()|}.ConfigureAwait(GetFlag());
            }
            catch (InvalidOperationException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ThrowingTaskLocalAwaitWrapperArgument_WithContinuingCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static bool GetFlag() => throw new InvalidOperationException();

        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            try
            {
                await first.ConfigureAwait(GetFlag());
            }
            catch (InvalidOperationException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ThrowingWhenAllAwaitWrapperArgument_WithContinuingCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static bool GetFlag() => throw new InvalidOperationException();

        public async Task Run(AppDbContext db)
        {
            try
            {
                await Task.WhenAll(
                    {|#0:db.Users.ToListAsync()|},
                    Task.CompletedTask).ConfigureAwait(GetFlag());
            }
            catch (InvalidOperationException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ConstantImmediateAwaitWrapperArgument_WithContinuingCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            try
            {
                await db.Users.ToListAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AwaitFirstInTry_WithContinuingCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                await first;
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReassignedTaskLocal_DropsActiveState()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            Task task = db.Users.ToListAsync();
            task = Task.CompletedTask;
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EscapedTaskLocal_DropsActiveState()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void Observe(Task task) { }

        public async Task Run(AppDbContext db)
        {
            var task = db.Users.ToListAsync();
            Observe(task);
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DirectTaskEscape_DropsActiveState()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void Observe(Task task) { }

        public async Task Run(AppDbContext db)
        {
            Observe(db.Users.ToListAsync());
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SiblingHelperArgumentsOverlapBeforeDirectEscape()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void Observe(Task first, Task second) { }

        public void Run(AppDbContext db)
        {
            Observe(
                {|#0:db.Users.ToListAsync()|},
                {|#1:db.Users.AnyAsync()|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DirectConstructorArgumentEscape_DropsActiveState()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class TaskHolder
    {
        public TaskHolder(Task task) { }
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            _ = new TaskHolder(db.Users.ToListAsync());
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ObjectInitializerRunsAfterConstructorArgumentEscape()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class TaskHolder
    {
        public TaskHolder(Task task, bool observe = true) { }
        public Task Later { get; set; } = Task.CompletedTask;
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            _ = new TaskHolder(db.Users.ToListAsync())
            {
                Later = db.Users.AnyAsync()
            };
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ObjectInitializerRunsAfterEmptyParamsConstructorArgumentEscape()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class TaskHolder
    {
        public TaskHolder(Task task, params object[] rest) { }
        public Task Later { get; set; } = Task.CompletedTask;
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            _ = new TaskHolder(db.Users.ToListAsync())
            {
                Later = db.Users.AnyAsync()
            };
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SiblingConstructorArgumentsOverlapBeforeDirectEscape()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class TaskHolder
    {
        public TaskHolder(Task first, Task second) { }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            _ = new TaskHolder(
                {|#0:db.Users.ToListAsync()|},
                {|#1:db.Users.AnyAsync()|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ReassignedContextAlias_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext first, AppDbContext second)
        {
            var alias = first;
            alias = second;
            await Task.WhenAll(alias.Users.ToListAsync(), alias.Users.AnyAsync());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ComputedContextProperty_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private readonly AppDbContext _db = new AppDbContext();
        private AppDbContext Current => _db;

        public async Task Run()
        {
            await Task.WhenAll(Current.Users.ToListAsync(), Current.Users.AnyAsync());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RepositoryProducedQuery_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static IQueryable<User> GetUsers(AppDbContext db) => db.Users;

        public async Task Run(AppDbContext db)
        {
            var query = GetUsers(db);
            await Task.WhenAll(query.ToListAsync(), query.AnyAsync());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReassignedContextParameter_BetweenOperations_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = db.Users.ToListAsync();
            db = other;
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExhaustiveSwitch_AwaitsTaskInEverySection_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, int choice)
        {
            var first = db.Users.ToListAsync();
            switch (choice)
            {
                case 0:
                    await first;
                    break;
                default:
                    await first;
                    break;
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NonExhaustiveSwitch_AwaitsTaskInOnlySection_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, int choice)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            switch (choice)
            {
                case 0:
                    await first;
                    break;
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ExactBaseException_CannotReachNarrowCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            try
            {
                _ = db.Users.ToListAsync();
                throw new Exception();
            }
            catch (InvalidOperationException)
            {
                await db.Users.AnyAsync();
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UnknownBaseException_MayReachNarrowCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static Exception GetError() => new InvalidOperationException();

        public async Task Run(AppDbContext db)
        {
            try
            {
                _ = {|#0:db.Users.ToListAsync()|};
                throw GetError();
            }
            catch (InvalidOperationException)
            {
                await {|#1:db.Users.AnyAsync()|};
            }
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ContextParameter_ReassignedByCapturedLocalFunction_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = db.Users.ToListAsync();
            Replace();
            await db.Users.AnyAsync();

            void Replace() => db = other;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RefMutatedExceptionLocal_MayReachNarrowCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void Replace(ref Exception error) =>
            error = new InvalidOperationException();

        public async Task Run(AppDbContext db)
        {
            Exception error = new Exception();
            Replace(ref error);
            try
            {
                _ = {|#0:db.Users.ToListAsync()|};
                throw error;
            }
            catch (InvalidOperationException)
            {
                await {|#1:db.Users.AnyAsync()|};
            }
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DeconstructionMutatedExceptionLocal_MayReachNarrowCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            Exception error = new Exception();
            (error, _) = (new InvalidOperationException(), 0);
            try
            {
                _ = {|#0:db.Users.ToListAsync()|};
                throw error;
            }
            catch (InvalidOperationException)
            {
                await {|#1:db.Users.AnyAsync()|};
            }
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CapturedExceptionLocal_WithForwardDeclaredWriter_MayReachNarrowCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            Exception error = new Exception();
            Replace();
            try
            {
                _ = {|#0:db.Users.ToListAsync()|};
                throw error;
            }
            catch (InvalidOperationException)
            {
                await {|#1:db.Users.AnyAsync()|};
            }

            void Replace() => error = new InvalidOperationException();
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task RefAliasMutatedExceptionLocal_MayReachNarrowCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            Exception error = new Exception();
            ref Exception alias = ref error;
            alias = new InvalidOperationException();
            try
            {
                _ = {|#0:db.Users.ToListAsync()|};
                throw error;
            }
            catch (InvalidOperationException)
            {
                _ = {|#1:db.Users.AnyAsync()|};
            }
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task RefAliasReassignedContextParameter_BetweenOperations_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public void Run(AppDbContext db, AppDbContext other)
        {
            _ = db.Users.ToListAsync();
            ref AppDbContext alias = ref db;
            alias = other;
            _ = db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReassignedMemberReceiverParameter_BetweenOperations_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public Holder(AppDbContext context) => Context = context;

        public AppDbContext Context { get; }
    }

    public sealed class Program
    {
        public async Task Run(Holder holder, Holder other)
        {
            _ = holder.Context.Users.ToListAsync();
            holder = other;
            await holder.Context.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RefMutatedTaskInput_NullBeforeWhenAll_ShouldKeepFirstTaskActive()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void Clear(ref Task task) => task = null;

        public async Task Run(AppDbContext db)
        {
            Task other = Task.CompletedTask;
            Clear(ref other);
            var first = {|#0:db.Users.ToListAsync()|};
            try
            {
                await Task.WhenAll(first, other);
            }
            catch (ArgumentException)
            {
                await {|#1:db.Users.AnyAsync()|};
            }
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task UnusedCapturedLambdaWriter_ShouldNotHideOverlap()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, AppDbContext other)
        {
            Action unused = () => db = other;
            _ = {|#0:db.Users.ToListAsync()|};
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task UnusedLocalFunctionWriter_ShouldNotHideOverlap()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = {|#0:db.Users.ToListAsync()|};
            await {|#1:db.Users.AnyAsync()|};

            void Replace() => db = other;
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ReassignedMutableDbSet_BetweenContexts_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class Order { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; } = new DbSet<User>();
        public DbSet<Order> Orders { get; set; } = new DbSet<Order>();

        public async Task Run(AppDbContext other)
        {
            Users = other.Users;
            _ = Users.ToListAsync();
            await Orders.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConstructorReassignedGetOnlyContext_BetweenOperations_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public Holder(AppDbContext first, AppDbContext second)
        {
            Context = first;
            _ = Context.Users.ToListAsync();
            Context = second;
            _ = Context.Users.AnyAsync();
        }

        public AppDbContext Context { get; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task InvokedFieldStoredWriter_ReassignsContextBeforeSecondOperation()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            _writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task WriterStoredOnDifferentReceiver_ShouldNotHideOverlap()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public Action Writer { get; set; } = () => { };
    }

    public sealed class Program
    {
        public async Task Run(
            AppDbContext db,
            AppDbContext other,
            Holder first,
            Holder second)
        {
            _ = {|#0:db.Users.ToListAsync()|};
            first.Writer = () => db = other;
            second.Writer();
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task OverwrittenFieldStoredWriter_ShouldNotHideOverlap()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = {|#0:db.Users.ToListAsync()|};
            _writer = () => db = other;
            _writer = () => { };
            _writer();
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task InvokedFieldStoredWriter_ThroughStableReceiverAlias_ReassignsContext()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public Action Writer { get; set; } = () => { };
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, AppDbContext other, Holder first)
        {
            _ = db.Users.ToListAsync();
            first.Writer = () => db = other;
            var alias = first;
            alias.Writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RefReboundFieldStoredWriter_ShouldNotHideOverlap()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public Action Writer { get; set; } = () => { };
    }

    public sealed class Program
    {
        public void Run(
            AppDbContext db,
            AppDbContext other,
            Holder first,
            Holder second)
        {
            _ = {|#0:db.Users.ToListAsync()|};
            first.Writer = () => db = other;
            ref Holder alias = ref first;
            alias = second;
            first.Writer();
            _ = {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ForwardDeclaredInvokedRebinder_ShouldNotHideOverlap()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public Action Writer { get; set; } = () => { };
    }

    public sealed class Program
    {
        public async Task Run(
            AppDbContext db,
            AppDbContext other,
            Holder first,
            Holder second)
        {
            _ = {|#0:db.Users.ToListAsync()|};
            first.Writer = () => db = other;
            Rebind();
            first.Writer();
            await {|#1:db.Users.AnyAsync()|};

            void Rebind() => first = second;
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task RefOverwrittenFieldStoredWriter_ShouldNotHideOverlap()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = {|#0:db.Users.ToListAsync()|};
            _writer = () => db = other;
            Clear(ref _writer);
            _writer();
            await {|#1:db.Users.AnyAsync()|};
        }

        private static void Clear(ref Action writer) => writer = () => { };
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NestedOverwriteOfFieldStoredWriter_ShouldNotHideOverlap()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _writer = () => db = other;
            Clear();
            _writer();
            _ = {|#0:db.Users.ToListAsync()|};
            await {|#1:db.Users.AnyAsync()|};

            void Clear() => _writer = () => { };
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task UninvokedOverwriteMethodReference_ShouldNotHideInvokedWriter()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            Action clear = Clear;
            _ = clear;
            _writer();
            await db.Users.AnyAsync();

            void Clear() => _writer = () => { };
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UninvokedReceiverRebinder_ShouldNotHideInvokedWriter()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public Action Writer { get; set; } = () => { };
    }

    public sealed class Program
    {
        public async Task Run(
            AppDbContext db,
            AppDbContext other,
            Holder first,
            Holder second)
        {
            _ = db.Users.ToListAsync();
            first.Writer = () => db = other;
            void Rebind() => first = second;
            first.Writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TransitivelyInvokedNestedOverwrite_ShouldNotHideOverlap()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _writer = () => db = other;
            Outer();
            _writer();
            _ = {|#0:db.Users.ToListAsync()|};
            await {|#1:db.Users.AnyAsync()|};

            void Outer() => Clear();
            void Clear() => _writer = () => { };
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ConditionalNestedOverwrite_ShouldNotHideInvokedWriter()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            Clear(false);
            _writer();
            await db.Users.AnyAsync();

            void Clear(bool apply)
            {
                if (apply)
                    _writer = () => { };
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DelegateInvokedLocalFunctionOverwrite_ShouldNotHideOverlap()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _writer = () => db = other;
            Action clear = Clear;
            clear();
            _writer();
            _ = {|#0:db.Users.ToListAsync()|};
            await {|#1:db.Users.AnyAsync()|};

            void Clear() => _writer = () => { };
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task FieldStoredAnonymousOverwrite_ShouldNotHideOverlap()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };
        private Action _clear = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _writer = () => db = other;
            _clear = () => _writer = () => { };
            _clear();
            _writer();
            _ = {|#0:db.Users.ToListAsync()|};
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NestedOverwriteUsesReceiverAtExecutionPosition()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public Action Writer { get; set; } = () => { };
    }

    public sealed class Program
    {
        public async Task Run(
            AppDbContext db,
            AppDbContext other,
            Holder first,
            Holder second)
        {
            var target = first;
            first.Writer = () => db = other;
            Clear();
            target = second;
            first.Writer();
            _ = {|#0:db.Users.ToListAsync()|};
            await {|#1:db.Users.AnyAsync()|};

            void Clear() => target.Writer = () => { };
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task OverwrittenClearDelegate_ShouldNotHideInvokedWriter()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };
        private Action _clear = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            _clear = () => _writer = () => { };
            _clear = () => { };
            _clear();
            _writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LaterMatchingNestedOverwriteExecution_ShouldNotHideOverlap()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public Action Writer { get; set; } = () => { };
    }

    public sealed class Program
    {
        public async Task Run(
            AppDbContext db,
            AppDbContext other,
            Holder first,
            Holder second)
        {
            var target = second;
            first.Writer = () => db = other;
            Clear();
            target = first;
            Clear();
            target = second;
            first.Writer();
            _ = {|#0:db.Users.ToListAsync()|};
            await {|#1:db.Users.AnyAsync()|};

            void Clear() => target.Writer = () => { };
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task HelperReceiverMutation_ShouldNotClaimDifferentStorageOverwrite()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public Action Writer { get; set; } = () => { };
    }

    public sealed class Program
    {
        public async Task Run(
            AppDbContext db,
            AppDbContext other,
            Holder first,
            Holder second)
        {
            var target = first;
            _ = db.Users.ToListAsync();
            first.Writer = () => db = other;
            Clear();
            first.Writer();
            await db.Users.AnyAsync();

            void Clear()
            {
                target = second;
                target.Writer = () => { };
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UnawaitedAsyncOverwrite_ShouldNotHideInvokedWriter()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            _ = ClearAsync();
            _writer();
            await db.Users.AnyAsync();

            async Task ClearAsync()
            {
                await Task.Yield();
                _writer = () => { };
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedUnusedReturn_ShouldNotHideDefiniteOverwrite()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _writer = () => db = other;
            Clear();
            _writer();
            _ = {|#0:db.Users.ToListAsync()|};
            await {|#1:db.Users.AnyAsync()|};

            void Clear()
            {
                Action unused = () => { return; };
                _ = unused;
                _writer = () => { };
            }
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ReceiverMemberWrite_ShouldNotHideInvokedWriter()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public Action Writer { get; set; } = () => { };
        public int Tag { get; set; }
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, AppDbContext other)
        {
            var holder = new Holder();
            _ = db.Users.ToListAsync();
            holder.Writer = () => db = other;
            holder.Tag = 1;
            holder.Writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReachingReceiverAssignment_ShouldNotUseHistoricalAlias()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public Action Writer { get; set; } = () => { };
    }

    public sealed class Program
    {
        public async Task Run(
            AppDbContext db,
            AppDbContext other,
            Holder first,
            Holder second)
        {
            var target = first;
            _ = db.Users.ToListAsync();
            first.Writer = () => db = other;
            target = second;
            target.Writer = () => { };
            first.Writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConditionalClearDelegate_ShouldNotHideInvokedWriter()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other, bool useClear)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            Action clear = useClear ? Clear : NoOp;
            clear();
            _writer();
            await db.Users.AnyAsync();

            void Clear() => _writer = () => { };
            void NoOp() { }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedDelegateInstallUsesReceiverAtExecutionPosition()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public Action Writer { get; set; } = () => { };
        public Action Clear { get; set; } = () => { };
    }

    public sealed class Program
    {
        public async Task Run(
            AppDbContext db,
            AppDbContext other,
            Holder first,
            Holder second)
        {
            var target = first;
            _ = db.Users.ToListAsync();
            first.Writer = () => db = other;
            target = second;
            Install();
            first.Clear();
            first.Writer();
            await db.Users.AnyAsync();

            void Install() => target.Clear = () => first.Writer = () => { };
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DelegatePassedAsArgument_ShouldNotCountAsInvoked()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            Consume(Clear);
            _writer();
            await db.Users.AnyAsync();

            void Clear() => _writer = () => { };
        }

        private static void Consume(Action action) { }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CoalesceSelectedClearDelegate_ShouldNotHideInvokedWriter()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other, Action maybe)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            Action clear = maybe ?? Clear;
            clear();
            _writer();
            await db.Users.AnyAsync();

            void Clear() => _writer = () => { };
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DeconstructedReceiverRebind_ShouldNotUseStaleAlias()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public Action Writer { get; set; } = () => { };
    }

    public sealed class Program
    {
        public async Task Run(
            AppDbContext db,
            AppDbContext other,
            Holder first,
            Holder second)
        {
            var target = first;
            _ = db.Users.ToListAsync();
            first.Writer = () => db = other;
            (target, _) = (second, 0);
            target.Writer = () => { };
            first.Writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RestoredSameDelegate_ShouldRemainRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            var saved = _writer;
            _writer = saved;
            _writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NoOpRefDelegateCall_ShouldKeepWriterRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            Touch(ref _writer);
            _writer();
            await db.Users.AnyAsync();
        }

        private static void Touch(ref Action writer) { }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DelegateFactoryArgument_ShouldNotCountAsInvoked()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            Action clear = ChooseNoOp(Clear);
            clear();
            _writer();
            await db.Users.AnyAsync();

            void Clear() => _writer = () => { };
        }

        private static Action ChooseNoOp(Action action) => () => { };
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReplacementInvokingSavedWriter_ShouldKeepWriterRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            var saved = _writer;
            _writer = () => saved();
            _writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EarlyReturnRefReplacement_ShouldKeepWriterRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            Clear(ref _writer, true);
            _writer();
            await db.Users.AnyAsync();
        }

        private static void Clear(ref Action writer, bool skip)
        {
            if (skip)
                return;

            writer = () => { };
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SameOriginReceiverReassignment_ShouldKeepWriterRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public Action Writer { get; set; } = () => { };
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, AppDbContext other, Holder first)
        {
            _ = db.Users.ToListAsync();
            first.Writer = () => db = other;
            var same = first;
            first = same;
            first.Writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RestoredWriterAfterIntermediateReplacement_ShouldRemainRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            var saved = _writer;
            _writer = () => { };
            _writer = saved;
            _writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReplacementEscapingSavedWriter_ShouldRemainRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            var saved = _writer;
            _writer = () => Invoke(saved);
            _writer();
            await db.Users.AnyAsync();
        }

        private static void Invoke(Action action) => action();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SameOriginLocalReceiverReassignment_ShouldKeepWriterRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public Action Writer { get; set; } = () => { };
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, AppDbContext other, Holder first)
        {
            _ = db.Users.ToListAsync();
            var holder = first;
            holder.Writer = () => db = other;
            var same = holder;
            holder = same;
            holder.Writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReplacementEscapingDelegateParameter_ShouldRemainRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other, Action saved)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            saved = _writer;
            _writer = () => Invoke(saved);
            _writer();
            await db.Users.AnyAsync();
        }

        private static void Invoke(Action action) => action();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RefReplacementEscapingSavedWriter_ShouldRemainRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            Replace(ref _writer, _writer);
            _writer();
            await db.Users.AnyAsync();
        }

        private static void Replace(ref Action writer, Action saved) =>
            writer = () => Invoke(saved);

        private static void Invoke(Action action) => action();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedRestorationAfterReceiverRebinding_ShouldRemainRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public Action Writer { get; set; } = () => { };
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, AppDbContext other, Holder first, Holder second)
        {
            _ = db.Users.ToListAsync();
            first.Writer = () => db = other;
            var saved = first.Writer;
            var target = second;
            void Restore() => target.Writer = saved;
            target = first;
            first.Writer = () => { };
            Restore();
            first.Writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConditionalSameOriginLocalReassignment_ShouldKeepWriterRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public Action Writer { get; set; } = () => { };
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, AppDbContext other, Holder first, bool flag)
        {
            _ = db.Users.ToListAsync();
            var holder = first;
            holder.Writer = () => db = other;
            if (flag)
                holder = first;
            holder.Writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RefExposedLocalReceiver_ShouldKeepWriterPotentiallyRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public Action Writer { get; set; } = () => { };
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, AppDbContext other, Holder first)
        {
            _ = db.Users.ToListAsync();
            var holder = first;
            holder.Writer = () => db = other;
            Touch(ref holder);
            holder.Writer();
            await db.Users.AnyAsync();
        }

        private static void Touch(ref Holder holder) { }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedSameOriginLocalWrite_ShouldKeepWriterRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public Action Writer { get; set; } = () => { };
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, AppDbContext other, Holder first)
        {
            _ = db.Users.ToListAsync();
            var holder = first;
            holder.Writer = () => db = other;
            void Touch() => holder = first;
            Touch();
            holder.Writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReplacementCallingSavedField_ShouldRemainRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };
        private Action _saved = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            _saved = _writer;
            _writer = () => InvokeSaved();
            _writer();
            await db.Users.AnyAsync();
        }

        private void InvokeSaved() => _saved();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OrdinaryCallRestoringWriter_ShouldKeepWriterRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };
        private Action _saved = () => { };

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            _saved = _writer;
            _writer = () => { };
            RestoreSaved();
            _writer();
            await db.Users.AnyAsync();
        }

        private void RestoreSaved() => _writer = _saved;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PropertyGetterRestoringWriter_ShouldKeepWriterRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };
        private Action _saved = () => { };

        private int Restore
        {
            get
            {
                _writer = _saved;
                return 0;
            }
        }

        public async Task Run(AppDbContext db, AppDbContext other)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            _saved = _writer;
            _writer = () => { };
            _ = Restore;
            _writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CustomSetterIgnoringReplacement_ShouldKeepWriterRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        private Action _writer = () => { };

        public bool IgnoreReplacement { get; set; }

        public Action Writer
        {
            get => _writer;
            set
            {
                if (!IgnoreReplacement)
                    _writer = value;
            }
        }
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, AppDbContext other, Holder holder)
        {
            _ = db.Users.ToListAsync();
            holder.Writer = () => db = other;
            holder.IgnoreReplacement = true;
            holder.Writer = () => { };
            holder.Writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OpaqueDelegateBetweenReplacementAndInvocation_ShouldKeepWriterPotentiallyRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };
        private Action _saved = () => { };

        public async Task Run(AppDbContext db, AppDbContext other, Action restore)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            _saved = _writer;
            _writer = () => { };
            restore();
            _writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task InterfaceSetterIgnoringReplacement_ShouldKeepWriterRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public interface IHolder
    {
        Action Writer { get; set; }
    }

    public sealed class Holder : IHolder
    {
        private Action _writer = () => { };

        public bool IgnoreReplacement { get; set; }

        Action IHolder.Writer
        {
            get => _writer;
            set
            {
                if (!IgnoreReplacement)
                    _writer = value;
            }
        }
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, AppDbContext other, Holder concrete)
        {
            _ = db.Users.ToListAsync();
            IHolder holder = concrete;
            holder.Writer = () => db = other;
            concrete.IgnoreReplacement = true;
            holder.Writer = () => { };
            holder.Writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task BreakBeforeReplacement_ShouldKeepWriterPotentiallyRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other, bool skipClear)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            do
            {
                if (skipClear)
                    break;

                _writer = () => { };
            }
            while (false);

            _writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ThrowExpressionBeforeReplacement_ShouldKeepWriterPotentiallyRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other, bool skipClear)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            try
            {
                _ = skipClear ? throw new InvalidOperationException() : 0;
                _writer = () => { };
            }
            catch (InvalidOperationException)
            {
            }

            _writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConditionalRightHandOperations_ShouldNotBeTreatedAsDefinitelyExecuted()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Consumer
    {
        public void Observe(Task<bool> task) { }
    }

    public sealed class Program
    {
        public async Task Coalesce(AppDbContext db, Task<bool> cached)
        {
            var first = cached ?? db.Users.AnyAsync();
            await db.Users.AnyAsync();
            await first;
        }

        public async Task LogicalAnd(AppDbContext db, bool shouldRun)
        {
            _ = shouldRun && db.Users.AnyAsync().IsCompleted;
            await db.Users.AnyAsync();
        }

        public async Task LogicalOr(AppDbContext db, bool skipRun)
        {
            _ = skipRun || db.Users.AnyAsync().IsCompleted;
            await db.Users.AnyAsync();
        }

        public async Task CoalesceAssignment(AppDbContext db, Task<bool> cached)
        {
            cached ??= db.Users.AnyAsync();
            await db.Users.AnyAsync();
            await cached;
        }

        public async Task NullConditional(AppDbContext db, Consumer consumer)
        {
            consumer?.Observe(db.Users.AnyAsync());
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NullConditionalReplacement_ShouldKeepWriterPotentiallyRunnable()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System;
	using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Consumer
    {
        public void Observe(Action replacement) { }
    }

    public sealed class Program
    {
        private Action _writer = () => { };

        public async Task Run(AppDbContext db, AppDbContext other, Consumer consumer)
        {
            _ = db.Users.ToListAsync();
            _writer = () => db = other;
            consumer?.Observe(_writer = () => { });
            _writer();
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SelfReferentialIncompleteQuery_DoesNotCrashAnalysis()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public async Task Run()
        {
            IQueryable<User> query = query.Where(user => true);
            await Task.WhenAll(query.ToListAsync(), query.AnyAsync());
        }
    }
}";

        var analyzerTest =
            new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
                LinqContraband.Analyzers.LC046_ConcurrentDbContextOperations.ConcurrentDbContextOperationsAnalyzer,
                Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
            {
                TestCode = test,
                CompilerDiagnostics = Microsoft.CodeAnalysis.Testing.CompilerDiagnostics.None
            };

        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task StableSingletonTaskArrayCheckedConversionCanReachCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, long value)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                int ignored = checked((int)value);
                await Task.WhenAny(tasks);
            }
            catch (OverflowException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StableSingletonTaskArrayCheckedConversionCaughtAsInvalidOperation_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, long value)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            try
            {
                int ignored = checked((int)value);
                await Task.WhenAny(tasks);
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StableSingletonTaskArrayUserDefinedConversionCanReachCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class ThrowingValue
    {
        public static implicit operator int(ThrowingValue value)
        {
            throw new InvalidOperationException();
        }
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, ThrowingValue value)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                int ignored = value;
                await Task.WhenAny(tasks);
            }
            catch (InvalidOperationException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StableSingletonTaskArrayUserDefinedOperatorCanReachCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class ThrowingValue
    {
        public static ThrowingValue operator +(ThrowingValue left, ThrowingValue right)
        {
            throw new InvalidOperationException();
        }
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, ThrowingValue value)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                var ignored = value + value;
                await Task.WhenAny(tasks);
            }
            catch (InvalidOperationException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DirectThrowingReplacementConstructor_ShouldReachOuterContinuingCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class ReplacementException : Exception
    {
        public ReplacementException()
        {
            throw new OutOfMemoryException();
        }
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            try
            {
                try
                {
                    Task[] tasks = { first };
                    await Task.WhenAny(tasks);
                }
                catch (OutOfMemoryException)
                {
                    throw new ReplacementException();
                }
            }
            catch (OutOfMemoryException)
            {
                await {|#1:db.Users.AnyAsync()|};
            }
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StoredSingletonTaskArrayWhenAnyOutsideTry_OomCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            var any = Task.WhenAny(tasks);
            try
            {
                await any;
            }
            catch (OutOfMemoryException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CustomEventAccessorBeforeTerminalReturn_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class ThrowingEvents
    {
        public event Action Changed
        {
            add { throw new InvalidOperationException(); }
            remove { }
        }
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, ThrowingEvents events, bool condition)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            try
            {
                if (condition)
                {
                    await first;
                }
                else
                {
                    events.Changed += () => { };
                    return;
                }
            }
            catch (InvalidOperationException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task UserDefinedArrayLengthConversionInterceptedByNestedCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class ThrowingLength
    {
        public static implicit operator int(ThrowingLength value)
        {
            throw new InvalidOperationException();
        }
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, ThrowingLength length)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            try
            {
                try
                {
                    var buffer = new byte[length];
                    await Task.WhenAny(tasks);
                }
                catch (Exception)
                {
                    return;
                }
            }
            catch (InvalidOperationException)
            {
                await db.Users.AnyAsync();
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskFromResultWithMismatchedCatchBeforeUnknownConsumer_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static void Consume(Task<Task<List<User>>> task)
        {
        }

        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            try
            {
                Consume(Task.FromResult(first));
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UserDefinedOperatorInterceptedByNestedCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class ThrowingValue
    {
        public static ThrowingValue operator +(ThrowingValue left, ThrowingValue right)
        {
            throw new InvalidOperationException();
        }
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, ThrowingValue value)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            try
            {
                try
                {
                    var ignored = value + value;
                    await Task.WhenAny(tasks);
                }
                catch
                {
                    return;
                }
            }
            catch (InvalidOperationException)
            {
                await db.Users.AnyAsync();
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SingletonTaskArrayThrowingPrefixesCanReachCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class ThrowingEvents
    {
        public event Action Changed
        {
            add { throw new InvalidOperationException(); }
            remove { }
        }
    }

    public sealed class ThrowingValue
    {
        public static ThrowingValue operator -(ThrowingValue value)
        {
            throw new InvalidOperationException();
        }
    }

    public sealed class Holder
    {
        public int Value;
    }

    public sealed class Program
    {
        public async Task EventAccessor(AppDbContext db, ThrowingEvents events)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                events.Changed += () => { };
                await Task.WhenAny(tasks);
            }
            catch (InvalidOperationException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }

        public async Task UnaryOperator(AppDbContext db, ThrowingValue value)
        {
            var first = {|#2:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                var ignored = -value;
                await Task.WhenAny(tasks);
            }
            catch (InvalidOperationException)
            {
            }

            await {|#3:db.Users.AnyAsync()|};
        }

        public async Task InstanceField(AppDbContext db, Holder holder)
        {
            var first = {|#4:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                var ignored = holder.Value;
                await Task.WhenAny(tasks);
            }
            catch (NullReferenceException)
            {
            }

            await {|#5:db.Users.AnyAsync()|};
        }
    }
}";

        var firstExpected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");
        var secondExpected = VerifyCS.Diagnostic()
            .WithLocation(3)
            .WithLocation(2)
            .WithArguments("db");
        var thirdExpected = VerifyCS.Diagnostic()
            .WithLocation(5)
            .WithLocation(4)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            firstExpected,
            secondExpected,
            thirdExpected);
    }

    [Fact]
    public async Task SingletonTaskArrayOverflowSafeCheckedConversions_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Widening(AppDbContext db, byte value)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            try
            {
                var ignored = checked((int)value);
                await Task.WhenAny(tasks);
            }
            catch (OverflowException)
            {
            }

            await db.Users.AnyAsync();
        }

        public async Task Constant(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            try
            {
                var ignored = checked((int)1L);
                await Task.WhenAny(tasks);
            }
            catch (OverflowException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SingletonTaskArrayKnownNonNullFieldReceiver_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class Holder
    {
        public int Value;
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            var holder = new Holder();
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            try
            {
                var ignored = holder.Value;
                await Task.WhenAny(tasks);
            }
            catch (NullReferenceException)
            {
            }

            await db.Users.AnyAsync();
        }

        public async Task MismatchedCatch(AppDbContext db, Holder holder)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            try
            {
                var ignored = holder.Value;
                await Task.WhenAny(tasks);
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SingletonTaskArrayFieldLikeEventAssignment_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public event EventHandler Changed;

        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            try
            {
                Changed += OnChanged;
                await Task.WhenAny(tasks);
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }

        private static void OnChanged(object sender, EventArgs args)
        {
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FieldLikeEventBeforeTerminalReturn_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public event Action Changed;

        public async Task Run(AppDbContext db, bool condition)
        {
            var first = db.Users.ToListAsync();
            try
            {
                if (condition)
                {
                    await first;
                }
                else
                {
                    Changed += () => { };
                    return;
                }
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DirectTaskElementInSingletonTaskArrayAwaitedWhenAny_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            Task[] tasks = { db.Users.ToListAsync() };
            await Task.WhenAny(tasks);
            await db.Users.AnyAsync();
        }

        public async Task SeparatelyAssigned(AppDbContext db)
        {
            Task[] tasks;
            tasks = new Task[] { db.Users.ToListAsync() };
            await Task.WhenAny(tasks);
            await db.Users.AnyAsync();
        }

        public async Task StoredCombinator(AppDbContext db)
        {
            Task[] tasks = { db.Users.ToListAsync() };
            var any = Task.WhenAny(tasks);
            await any;
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DirectTaskElementInSingletonTaskArrayOomCatch_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            try
            {
                Task[] tasks = { {|#0:db.Users.ToListAsync()|} };
                await Task.WhenAny(tasks);
            }
            catch (OutOfMemoryException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task FieldLikeEventWithNullableReceiverBeforeSingletonWhenAny_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class Holder
    {
        public event EventHandler Changed;
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, Holder holder)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                holder.Changed += OnChanged;
                await Task.WhenAny(tasks);
            }
            catch (NullReferenceException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }

        public async Task MismatchedCatch(AppDbContext db, Holder holder)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            try
            {
                holder.Changed += OnChanged;
                await Task.WhenAny(tasks);
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.AnyAsync();
        }

        private static void OnChanged(object sender, EventArgs args)
        {
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TerminalOppositeBranchWithUnhandledPrefixException_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool condition, int value)
        {
            var first = db.Users.ToListAsync();
            if (condition)
            {
                await first;
            }
            else
            {
                _ = value + 1;
                return;
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SingletonTaskArrayNullConditionalFieldReceiver_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class Holder
    {
        public int Value;
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, Holder holder)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            try
            {
                var ignored = holder?.Value;
                await Task.WhenAny(tasks);
            }
            catch (NullReferenceException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SingletonTaskArrayBuiltInExceptionPaths_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task ReferenceCast(AppDbContext db, object value)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                var text = (string)value;
                await Task.WhenAny(tasks);
            }
            catch (InvalidCastException)
            {
            }

            await {|#1:db.Users.AnyAsync()|};
        }

        public async Task CheckedAddition(AppDbContext db, int value)
        {
            var first = {|#2:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                var sum = checked(value + 1);
                await Task.WhenAny(tasks);
            }
            catch (OverflowException)
            {
            }

            await {|#3:db.Users.AnyAsync()|};
        }

        public async Task Division(AppDbContext db, int divisor)
        {
            var first = {|#4:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                var quotient = 1 / divisor;
                await Task.WhenAny(tasks);
            }
            catch (DivideByZeroException)
            {
            }

            await {|#5:db.Users.AnyAsync()|};
        }

        public async Task CheckedIncrement(AppDbContext db, int value)
        {
            var first = {|#6:db.Users.ToListAsync()|};
            Task[] tasks = { first };
            try
            {
                checked
                {
                    value++;
                }

                await Task.WhenAny(tasks);
            }
            catch (OverflowException)
            {
            }

            await {|#7:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");
        var expectedCheckedAddition = VerifyCS.Diagnostic()
            .WithLocation(3)
            .WithLocation(2)
            .WithArguments("db");
        var expectedDivision = VerifyCS.Diagnostic()
            .WithLocation(5)
            .WithLocation(4)
            .WithArguments("db");
        var expectedCheckedIncrement = VerifyCS.Diagnostic()
            .WithLocation(7)
            .WithLocation(6)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            expected,
            expectedCheckedAddition,
            expectedDivision,
            expectedCheckedIncrement);
    }

    [Fact]
    public async Task TerminalOppositeBranchWithSafeBuiltInPrefixes_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task ReferenceUpcast(AppDbContext db, bool condition)
        {
            var first = db.Users.ToListAsync();
            try
            {
                if (condition)
                {
                    await first;
                }
                else
                {
                    object value = ""safe"";
                    return;
                }
            }
            catch (InvalidCastException)
            {
            }

            await db.Users.AnyAsync();
        }

        public async Task WideningNumericConversion(AppDbContext db, bool condition, int input)
        {
            var first = db.Users.ToListAsync();
            try
            {
                if (condition)
                {
                    await first;
                }
                else
                {
                    long value = input;
                    return;
                }
            }
            catch (OverflowException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SingletonTaskArrayBoundedUnsignedLengthWithOverflowCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task ByteLength(AppDbContext db, byte length)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            try
            {
                var scratch = new Task[length];
                await Task.WhenAny(tasks);
            }
            catch (OverflowException)
            {
            }

            await db.Users.AnyAsync();
        }

        public async Task UShortLength(AppDbContext db, ushort length)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            try
            {
                var scratch = new Task[length];
                await Task.WhenAny(tasks);
            }
            catch (OverflowException)
            {
            }

            await db.Users.AnyAsync();
        }

        public async Task CharLength(AppDbContext db, char length)
        {
            var first = db.Users.ToListAsync();
            Task[] tasks = { first };
            try
            {
                var scratch = new Task[length];
                await Task.WhenAny(tasks);
            }
            catch (OverflowException)
            {
            }

            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
