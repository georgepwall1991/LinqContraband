using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC046_ConcurrentDbContextOperations.ConcurrentDbContextOperationsAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC046_ConcurrentDbContextOperations;

public sealed class ConcurrentDbContextOperationsLoopTests
{
    private const string EfMock = ConcurrentDbContextOperationsTests.EfMock;

    [Fact]
    public async Task ForeachOverTwoElementArray_WithDiscardedTask_ShouldTrigger()
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
        public void Run(AppDbContext db)
        {
            foreach (var id in new[] { 1, 2 })
            {
                _ = {|#0:db.Users.ToListAsync()|};
            }
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ForeachWithoutProvenRepeatedDiscard_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System;
	using System.Collections.Generic;
	using System.Threading;
	using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public void Singleton(AppDbContext db)
        {
            foreach (var id in new[] { 1 })
            {
                _ = db.Users.ToListAsync();
            }
        }

        public void Unknown(AppDbContext db, IEnumerable<int> ids)
        {
            foreach (var id in ids)
            {
                _ = db.Users.ToListAsync();
            }
        }

        public async Task Awaited(AppDbContext db)
        {
            foreach (var id in new[] { 1, 2 })
            {
                await db.Users.ToListAsync();
            }
        }

        public async Task DiscardedAwaitedResult(AppDbContext db)
        {
            foreach (var id in new[] { 1, 2 })
            {
                _ = await db.Users.ToListAsync();
            }
        }

        public void RefArgumentRebindsContext(AppDbContext db)
        {
            foreach (var id in new[] { 1, 2 })
            {
                _ = db.Users.ToListAsync(Swap(ref db));
            }
        }

        public void DynamicRefArgumentRebindsContext(AppDbContext db, dynamic swapper)
        {
            foreach (var id in new[] { 1, 2 })
            {
                _ = db.Users.ToListAsync((CancellationToken)swapper.Swap(ref db));
            }
        }

        public void InvokedLocalFunctionRebindsContext(AppDbContext db, AppDbContext other)
        {
            int Rebind()
            {
                db = other;
                return 0;
            }

            foreach (var id in new[] { 1, 2 })
            {
                _ = db.Users.ElementAtAsync(Rebind());
            }
        }

        public void InvokedDelegateRebindsContext(AppDbContext db, AppDbContext other)
        {
            Func<int> rebind = () =>
            {
                db = other;
                return 0;
            };

            foreach (var id in new[] { 1, 2 })
            {
                _ = db.Users.ElementAtAsync(rebind());
            }
        }

        public void TransitivelyInvokedLocalFunctionRebindsContext(
            AppDbContext db,
            AppDbContext other)
        {
            int Rebind()
            {
                db = other;
                return 0;
            }

            int GetIndex() => Rebind();

            foreach (var id in new[] { 1, 2 })
            {
                _ = db.Users.ElementAtAsync(GetIndex());
            }
        }

        public void ConditionalDelegateRebindsContext(
            AppDbContext db,
            AppDbContext other,
            bool first)
        {
            Func<int> rebind = first
                ? () =>
                {
                    db = other;
                    return 0;
                }
                : () =>
                {
                    db = other;
                    return 1;
                };

            foreach (var id in new[] { 1, 2 })
            {
                _ = db.Users.ElementAtAsync(rebind());
            }
        }

        public void MulticastDelegateRebindsContext(AppDbContext db, AppDbContext other)
        {
            Func<int> rebind = () => 0;
            rebind += () =>
            {
                db = other;
                return 1;
            };

            foreach (var id in new[] { 1, 2 })
            {
                _ = db.Users.ElementAtAsync(rebind());
            }
        }

        private static CancellationToken Swap(ref AppDbContext db)
        {
            db = new AppDbContext();
            return default;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ForeachWithConditionalExitOrPerIterationContext_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
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
        public void Conditional(AppDbContext db)
        {
            foreach (var id in new[] { 1, 2 })
            {
                if (id > 0)
                    _ = db.Users.ToListAsync();
            }
        }

        public void Breaks(AppDbContext db)
        {
            foreach (var id in new[] { 1, 2 })
            {
                _ = db.Users.ToListAsync();
                break;
            }
        }

        public void PerIterationContext()
        {
            foreach (var id in new[] { 1, 2 })
            {
                var db = new AppDbContext();
                _ = db.Users.ToListAsync();
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ForeachWithUninvokedWriterOrInvokedNonWriter_ShouldStillTrigger()
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
        public void UninvokedWriter(AppDbContext db, AppDbContext other)
        {
            int Rebind()
            {
                db = other;
                return 0;
            }

            foreach (var id in new[] { 1, 2 })
            {
                _ = {|#0:db.Users.ElementAtAsync(id)|};
            }
        }

        public void InvokedNonWriter(AppDbContext db)
        {
            int GetIndex() => 0;

            foreach (var id in new[] { 1, 2 })
            {
                _ = {|#1:db.Users.ElementAtAsync(GetIndex())|};
            }
        }

        public void MethodReferenceNotInvoked(AppDbContext db, AppDbContext other)
        {
            int Rebind()
            {
                db = other;
                return 0;
            }

            foreach (var id in new[] { 1, 2 })
            {
                _ = {|#2:db.Users.ElementAtAsync(Ignore(Rebind))|};
            }
        }

        private static int Ignore(Func<int> action) => 0;
    }
}";

        var uninvokedWriter = VerifyCS.Diagnostic()
            .WithLocation(0)
            .WithArguments("db");
        var invokedNonWriter = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithArguments("db");
        var methodReferenceNotInvoked = VerifyCS.Diagnostic()
            .WithLocation(2)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            uninvokedWriter,
            invokedNonWriter,
            methodReferenceNotInvoked);
    }

    [Fact]
    public async Task ForeachWithTwoInvocationSyntaxes_ShouldReportOnlyDirectOverlap()
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
        public void Run(AppDbContext db)
        {
            foreach (var id in new[] { 1, 2 })
            {
                _ = {|#0:db.Users.ToListAsync()|};
                _ = {|#1:db.Users.AnyAsync()|};
            }
        }

        public void PriorOperation(AppDbContext db)
        {
            _ = {|#2:db.Users.AnyAsync()|};
            foreach (var id in new[] { 1, 2 })
            {
                _ = {|#3:db.Users.ToListAsync()|};
            }
        }

        public void PriorExhaustiveBranches(AppDbContext db, bool first)
        {
            if (first)
                _ = {|#4:db.Users.ToListAsync()|};
            else
                _ = {|#5:db.Users.AnyAsync()|};

            foreach (var id in new[] { 1, 2 })
            {
                _ = {|#6:db.Users.ToListAsync()|};
            }
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        var priorOperation = VerifyCS.Diagnostic()
            .WithLocation(3)
            .WithLocation(2)
            .WithArguments("db");
        var priorBranches = VerifyCS.Diagnostic()
            .WithLocation(6)
            .WithLocation(4)
            .WithLocation(5)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected, priorOperation, priorBranches);
    }
}
