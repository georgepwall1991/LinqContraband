using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC046_ConcurrentDbContextOperations.ConcurrentDbContextOperationsAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC046_ConcurrentDbContextOperations;

public sealed class ConcurrentDbContextOperationsControlFlowTests
{
    private const string EfMock = ConcurrentDbContextOperationsTests.EfMock;

    [Fact]
    public async Task ExhaustiveBranchStarts_BeforeLaterOperation_ShouldTrigger()
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
        public async Task Run(AppDbContext db, bool useFirst)
        {
            if (useFirst)
                _ = {|#0:db.Users.ToListAsync()|};
            else
                _ = {|#1:db.Users.AnyAsync()|};

            await {|#2:db.Users.ToListAsync()|};
        }

        public async Task BareCalls(AppDbContext db, bool useFirst)
        {
#pragma warning disable CS4014
            if (useFirst)
                {|#3:db.Users.ToListAsync()|};
            else
                {|#4:db.Users.AnyAsync()|};
#pragma warning restore CS4014

            await {|#5:db.Users.ToListAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(2)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("db");

        var bareCalls = VerifyCS.Diagnostic()
            .WithLocation(5)
            .WithLocation(3)
            .WithLocation(4)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected, bareCalls);
    }

    [Fact]
    public async Task ExhaustiveBranchTaskLocalAssignments_BeforeLaterOperation_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Collections.Generic;
	using System.Runtime.CompilerServices;
	using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, bool useFirst)
        {
            Task pending;
            if (useFirst)
                pending = {|#0:db.Users.ToListAsync()|};
            else
                pending = {|#1:db.Users.AnyAsync()|};

            await {|#2:db.Users.ToListAsync()|};
            await pending;
        }

        public async Task WrappedFind(AppDbContext db, bool useFirst)
        {
            Task pending;
            if (useFirst)
                pending = {|#3:db.FindAsync<User>(1)|}.AsTask();
            else
                pending = {|#4:db.FindAsync<User>(2)|}.AsTask();

            await {|#5:db.SaveChangesAsync()|};
            await pending;
        }

        public async Task ConfiguredAwaitable(AppDbContext db, bool useFirst)
        {
            ConfiguredTaskAwaitable<List<User>> pending;
            if (useFirst)
                pending = {|#6:db.Users.ToListAsync()|}.ConfigureAwait(false);
            else
                pending = {|#7:db.Users.ToListAsync()|}.ConfigureAwait(false);

            await {|#8:db.Users.AnyAsync()|};
            await pending;
        }

        public async Task DistinctLocals(AppDbContext db, bool useFirst)
        {
            Task first;
            Task second;
            if (useFirst)
                first = {|#9:db.Users.ToListAsync()|};
            else
                second = {|#10:db.Users.AnyAsync()|};

            await {|#11:db.Users.ToListAsync()|};
        }

        public async Task DistinctLocalsWithUninvokedRead(AppDbContext db, bool useFirst)
        {
            Task first;
            Task second;
            if (useFirst)
            {
                first = {|#12:db.Users.ToListAsync()|};
                System.Func<bool> inspect = () => first.IsCompleted;
            }
            else
            {
                second = {|#13:db.Users.AnyAsync()|};
            }

            await {|#14:db.Users.ToListAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(2)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("db");

        var wrappedFind = VerifyCS.Diagnostic()
            .WithLocation(5)
            .WithLocation(3)
            .WithLocation(4)
            .WithArguments("db");

        var configuredAwaitable = VerifyCS.Diagnostic()
            .WithLocation(8)
            .WithLocation(6)
            .WithLocation(7)
            .WithArguments("db");

        var distinctLocals = VerifyCS.Diagnostic()
            .WithLocation(11)
            .WithLocation(9)
            .WithLocation(10)
            .WithArguments("db");

        var uninvokedRead = VerifyCS.Diagnostic()
            .WithLocation(14)
            .WithLocation(12)
            .WithLocation(13)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            expected,
            wrappedFind,
            configuredAwaitable,
            distinctLocals,
            uninvokedRead);
    }

    [Fact]
    public async Task ExhaustiveBranchesWithoutSameActiveOrigin_ShouldNotTrigger()
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
        public async Task DifferentOrigin(AppDbContext first, AppDbContext second, bool useFirst)
        {
            if (useFirst)
                _ = first.Users.ToListAsync();
            else
                _ = second.Users.ToListAsync();

            await first.Users.AnyAsync();
        }

        public async Task OneBranchCompletes(AppDbContext db, bool complete)
        {
            if (complete)
                await db.Users.ToListAsync();
            else
                _ = db.Users.AnyAsync();

            await db.Users.ToListAsync();
        }

        public async Task OptionalNestedStart(AppDbContext db, bool outer, bool inner)
        {
            if (outer)
            {
                if (inner)
                    _ = db.Users.ToListAsync();
            }
            else
            {
                _ = db.Users.AnyAsync();
            }

            await db.Users.ToListAsync();
        }

        public async Task ContinueTransfer(AppDbContext db, bool repeat, bool first)
        {
            while (repeat)
            {
                if (first)
                {
                    _ = db.Users.ToListAsync();
                    continue;
                }
                else
                {
                    _ = db.Users.AnyAsync();
                }

                await db.Users.ToListAsync();
                break;
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExhaustiveBranchTaskLocalCompletedMutatedOrEscaped_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Collections.Generic;
	using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public static class TaskExtensions
    {
        public static Task AsTask<T>(this Task<T> task)
        {
            task.GetAwaiter().GetResult();
            return Task.CompletedTask;
        }
    }

    public sealed class User { }

    public sealed class CompletionSink
    {
        public static implicit operator CompletionSink(Task<List<User>> task)
        {
            task.GetAwaiter().GetResult();
            return new CompletionSink();
        }

        public static implicit operator CompletionSink(Task<bool> task)
        {
            task.GetAwaiter().GetResult();
            return new CompletionSink();
        }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Completed(AppDbContext db, bool useFirst)
        {
            Task pending;
            if (useFirst)
                pending = db.Users.ToListAsync();
            else
                pending = db.Users.AnyAsync();

            await pending;
            await db.Users.ToListAsync();
        }

        public async Task Reassigned(AppDbContext db, bool useFirst)
        {
            Task pending;
            if (useFirst)
                pending = db.Users.ToListAsync();
            else
                pending = db.Users.AnyAsync();

            pending = Task.CompletedTask;
            await db.Users.ToListAsync();
        }

        public async Task Escaped(AppDbContext db, bool useFirst)
        {
            Task pending;
            if (useFirst)
                pending = db.Users.ToListAsync();
            else
                pending = db.Users.AnyAsync();

            Observe(pending);
            await db.Users.ToListAsync();
        }

        public async Task ByRefMutation(AppDbContext db, bool useFirst)
        {
            Task pending;
            if (useFirst)
                pending = db.Users.ToListAsync();
            else
                pending = db.Users.AnyAsync();

            Reset(ref pending);
            await db.Users.ToListAsync();
        }

        public async Task DistinctLocalOneBranchCompleted(AppDbContext db, bool useFirst)
        {
            Task first;
            Task second;
            if (useFirst)
            {
                first = db.Users.ToListAsync();
                await first;
            }
            else
            {
                second = db.Users.AnyAsync();
            }

            await db.Users.ToListAsync();
        }

        public async Task DistinctLocalOneBranchEscaped(AppDbContext db, bool useFirst)
        {
            Task first;
            Task second;
            if (useFirst)
            {
                first = db.Users.ToListAsync();
                Observe(first);
            }
            else
            {
                second = db.Users.AnyAsync();
            }

            await db.Users.ToListAsync();
        }

        public async Task ChainedAliasCompleted(AppDbContext db, bool useFirst)
        {
            Task pending;
            Task alias;
            if (useFirst)
                alias = pending = db.Users.ToListAsync();
            else
                alias = pending = db.Users.AnyAsync();

            await alias;
            await db.Users.ToListAsync();
        }

        public async Task CustomAsTaskCompletesOperation(AppDbContext db, bool useFirst)
        {
            Task pending;
            if (useFirst)
                pending = db.Users.ToListAsync().AsTask();
            else
                pending = db.Users.AnyAsync().AsTask();

            await db.Users.ToListAsync();
            await pending;
        }

        public async Task UserDefinedConversionCompletesOperation(
            AppDbContext db,
            bool useFirst)
        {
            CompletionSink pending;
            if (useFirst)
                pending = db.Users.ToListAsync();
            else
                pending = db.Users.AnyAsync();

            await db.Users.ToListAsync();
            _ = pending;
        }

        private static void Observe(Task task) { }
        private static void Reset(ref Task task) => task = Task.CompletedTask;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExhaustiveBranchStartBypassedByContinuingCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
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
        public async Task Run(AppDbContext db, bool useFirst)
        {
            try
            {
                if (useFirst)
                {
                    MayThrow();
                    _ = db.Users.ToListAsync();
                }
                else
                {
                    _ = db.Users.AnyAsync();
                }
            }
            catch (InvalidOperationException)
            {
            }

            await db.Users.ToListAsync();
        }

        public async Task NestedCatch(AppDbContext db, bool useFirst)
        {
            if (useFirst)
            {
                try
                {
                    MayThrow();
                    _ = db.Users.ToListAsync();
                }
                catch (InvalidOperationException)
                {
                }
            }
            else
            {
                _ = db.Users.AnyAsync();
            }

            await db.Users.ToListAsync();
        }

        public async Task CurrentInFinally(AppDbContext db, bool useFirst)
        {
            try
            {
                if (useFirst)
                    _ = db.Users.ToListAsync();
                else
                    _ = db.Users.AnyAsync();
            }
            finally
            {
                await db.Users.ToListAsync();
            }
        }

        private static void MayThrow() => throw new InvalidOperationException();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SiblingBranchTransfer_ShouldNotRouteBranchLocalStartIntoCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
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
        public async Task ExplicitThrow(AppDbContext db, bool start)
        {
            try
            {
                if (start)
                    _ = db.Users.ToListAsync();
                else
                    throw new InvalidOperationException();
            }
            catch (InvalidOperationException)
            {
                await db.Users.AnyAsync();
            }
        }

        public async Task ThrowingCall(AppDbContext db, bool start)
        {
            try
            {
                if (start)
                    _ = db.Users.ToListAsync();
                else
                    MayThrow();
            }
            catch (InvalidOperationException)
            {
                await db.Users.AnyAsync();
            }
        }

        private static void MayThrow() => throw new InvalidOperationException();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SameBranchThrowAfterBranchLocalStart_ShouldTriggerInCatch()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
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
        public async Task Run(AppDbContext db, bool start)
        {
            try
            {
                if (start)
                {
                    _ = {|#0:db.Users.ToListAsync()|};
                    throw new InvalidOperationException();
                }
                else
                {
                    return;
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
    public async Task ConditionalForwardGotoSkippingFirstOperation_ShouldNotTrigger()
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
        public async Task Run(AppDbContext db, bool skip)
        {
            if (skip)
                goto Next;

            _ = db.Users.ToListAsync();

        Next:
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NonBypassingOrNestedGoto_ShouldKeepDirectOverlap()
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
        public async Task TargetAfterBoth(AppDbContext db, bool skip)
        {
            if (skip)
                goto Done;

            _ = {|#0:db.Users.ToListAsync()|};
            await {|#1:db.Users.AnyAsync()|};

        Done:
            return;
        }

        public async Task NestedExecutable(AppDbContext db)
        {
            void Local()
            {
                goto Shared;
            Shared:
                return;
            }

            _ = {|#2:db.Users.ToListAsync()|};
            await {|#3:db.Users.AnyAsync()|};
        }
    }
}";

        var first = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");
        var second = VerifyCS.Diagnostic()
            .WithLocation(3)
            .WithLocation(2)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, first, second);
    }
}
