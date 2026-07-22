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
}
