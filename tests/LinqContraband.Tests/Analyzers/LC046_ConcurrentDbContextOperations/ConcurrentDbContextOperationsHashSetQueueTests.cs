using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC046_ConcurrentDbContextOperations.ConcurrentDbContextOperationsAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC046_ConcurrentDbContextOperations;

public sealed class ConcurrentDbContextOperationsHashSetQueueTests
{
    private const string EfMock = ConcurrentDbContextOperationsTests.EfMock;

    [Fact]
    public async Task ForeachOverTwoElementArray_AddsEfTasksToFreshHashSet_ShouldTrigger()
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
        public async Task TypedHashSet(AppDbContext db)
        {
            var tasks = new HashSet<Task<User>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add({|#0:db.Users.ElementAtAsync(id)|});
            }

            await Task.WhenAll(tasks);
        }

        public async Task UntypedHashSet(AppDbContext db)
        {
            var tasks = new HashSet<Task>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add({|#1:db.Users.AnyAsync()|});
            }

            await Task.WhenAll(tasks);
        }
    }
}";

        var typed = VerifyCS.Diagnostic()
            .WithLocation(0)
            .WithArguments("db");
        var untyped = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, typed, untyped);
    }

    [Fact]
    public async Task ForeachOverTwoElementArray_EnqueuesEfTasksToFreshQueue_ShouldTrigger()
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
            var tasks = new Queue<Task>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Enqueue({|#0:db.Users.AnyAsync()|});
            }

            await Task.WhenAll(tasks);
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ForeachOverTwoElementArray_AddsEfTasksToHashSetThroughCollectionInterface_ShouldTrigger()
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
            ICollection<Task> tasks = new HashSet<Task>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add({|#0:db.Users.AnyAsync()|});
            }

            await Task.WhenAll(tasks);
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ForeachTaskHashSetQueue_WithUnprovenAccumulators_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
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
        public async Task ConcurrentBag(AppDbContext db)
        {
            var tasks = new ConcurrentBag<Task>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.AnyAsync());
            }

            await Task.WhenAll(tasks);
        }

        public async Task EscapedBeforeWhenAll(AppDbContext db)
        {
            var tasks = new HashSet<Task>();
            Observe(tasks);
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.AnyAsync());
            }

            await Task.WhenAll(tasks);
        }

        public async Task ReassignedAccumulator(AppDbContext db)
        {
            var tasks = new HashSet<Task>();
            tasks = new HashSet<Task>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.AnyAsync());
            }

            await Task.WhenAll(tasks);
        }

        public async Task InterfaceCollectionExpression(AppDbContext db)
        {
            ICollection<Task> tasks = [];
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.AnyAsync());
            }

            await Task.WhenAll(tasks);
        }

        public async Task HashSetFromExistingCollection(AppDbContext db)
        {
            var tasks = new HashSet<Task>(new Task[0]);
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.AnyAsync());
            }

            await Task.WhenAll(tasks);
        }

        public async Task QueueFromExistingCollection(AppDbContext db)
        {
            var tasks = new Queue<Task>(new Task[0]);
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Enqueue(db.Users.AnyAsync());
            }

            await Task.WhenAll(tasks);
        }

        public async Task UnknownCollectionInterface(AppDbContext db, ICollection<Task> tasks)
        {
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.AnyAsync());
            }

            await Task.WhenAll(tasks);
        }

        public async Task MixedNonEfThenEfEnqueue(AppDbContext db)
        {
            var tasks = new Queue<Task>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Enqueue(Task.CompletedTask);
                tasks.Enqueue(db.Users.AnyAsync());
            }

            await Task.WhenAll(tasks);
        }

        private static void Observe(HashSet<Task> tasks) { }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ForeachTaskHashSetQueue_DrainsPreviousTasksBeforeStartingNext_ShouldNotTrigger()
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
        public void HashSetDirect(AppDbContext db)
        {
            var tasks = new HashSet<Task<User>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.ElementAtAsync(DrainHashSet(tasks)));
            }
        }

        public void QueueDirect(AppDbContext db)
        {
            var tasks = new Queue<Task<User>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Enqueue(db.Users.ElementAtAsync(DrainQueue(tasks)));
            }
        }

        public void HashSetCapturedLocalFunction(AppDbContext db)
        {
            var tasks = new HashSet<Task<User>>();

            int DrainCaptured()
            {
                Task.WhenAll(tasks).GetAwaiter().GetResult();
                return 0;
            }

            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.ElementAtAsync(DrainCaptured()));
            }
        }

        public void QueueCapturedLocalFunction(AppDbContext db)
        {
            var tasks = new Queue<Task<User>>();

            int DrainCaptured()
            {
                Task.WhenAll(tasks).GetAwaiter().GetResult();
                return 0;
            }

            foreach (var id in new[] { 1, 2 })
            {
                tasks.Enqueue(db.Users.ElementAtAsync(DrainCaptured()));
            }
        }

        private static int DrainHashSet(HashSet<Task<User>> tasks)
        {
            Task.WhenAll(tasks).GetAwaiter().GetResult();
            return 0;
        }

        private static int DrainQueue(Queue<Task<User>> tasks)
        {
            Task.WhenAll(tasks).GetAwaiter().GetResult();
            return 0;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public void LoopAccumulatorProof_AllowsHashSetAddAndQueueEnqueue()
    {
        var analysisPath = Path.Combine(
            LinqContraband.Tests.Architecture.RepositoryLayout.GetRepositoryRoot(),
            "src",
            "LinqContraband",
            "Analyzers",
            "ExecutionAndAsync",
            "LC046_ConcurrentDbContextOperations",
            "ConcurrentDbContextOperationsLoopAnalysis.cs");
        var source = File.ReadAllText(analysisPath);
        Assert.Contains("System.Collections.Generic.HashSet`1", source, StringComparison.Ordinal);
        Assert.Contains("System.Collections.Generic.Queue`1", source, StringComparison.Ordinal);
        Assert.Contains("Enqueue", source, StringComparison.Ordinal);
    }
}
