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
