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
