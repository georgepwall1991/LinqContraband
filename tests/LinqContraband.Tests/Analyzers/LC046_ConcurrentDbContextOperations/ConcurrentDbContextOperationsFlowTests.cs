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
