using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC046_ConcurrentDbContextOperations.ConcurrentDbContextOperationsAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC046_ConcurrentDbContextOperations;

public sealed class ConcurrentDbContextOperationsHashSetQueueAssignmentTests
{
    private const string EfMock = ConcurrentDbContextOperationsTests.EfMock;

    [Fact]
    public async Task ForeachOverTwoElementArray_AddsEfTasksToLaterAssignedHashSetAndQueue_ShouldTrigger()
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
        public async Task HashSetAssignment(AppDbContext db)
        {
            HashSet<Task> tasks;
            tasks = new HashSet<Task>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add({|#0:db.Users.AnyAsync()|});
            }

            await Task.WhenAll(tasks);
        }

        public async Task QueueAssignment(AppDbContext db)
        {
            Queue<Task> tasks;
            tasks = new Queue<Task>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Enqueue({|#1:db.Users.AnyAsync()|});
            }

            await Task.WhenAll(tasks);
        }

        public async Task HashSetThroughCollectionInterface(AppDbContext db)
        {
            ICollection<Task> tasks;
            tasks = new HashSet<Task>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add({|#2:db.Users.AnyAsync()|});
            }

            await Task.WhenAll(tasks);
        }
    }
}";

        var hashSet = VerifyCS.Diagnostic()
            .WithLocation(0)
            .WithArguments("db");
        var queue = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithArguments("db");
        var collection = VerifyCS.Diagnostic()
            .WithLocation(2)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, hashSet, queue, collection);
    }

    [Fact]
    public async Task ForeachOverTwoElementArray_AddsEfTasksToEmptyHashSetObjectInitializer_ShouldTrigger()
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
            var tasks = new HashSet<Task> { };
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
    public async Task ForeachTaskHashSetQueue_WithSeededOrLookalikeAccumulators_ShouldNotTrigger()
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
        public async Task SeededHashSetInitializer(AppDbContext db)
        {
            var tasks = new HashSet<Task> { Task.CompletedTask };
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.AnyAsync());
            }

            await Task.WhenAll(tasks);
        }

        public async Task SeededHashSetCollectionExpression(AppDbContext db)
        {
            HashSet<Task> tasks = [Task.CompletedTask];
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.AnyAsync());
            }

            await Task.WhenAll(tasks);
        }

        public async Task HashSetThroughSetInterface(AppDbContext db)
        {
            // ISet<T>.Add is ISet`1, not HashSet`1 / ICollection`1.
            ISet<Task> tasks = new HashSet<Task>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.AnyAsync());
            }

            await Task.WhenAll(tasks);
        }

        public async Task ConcurrentQueue(AppDbContext db)
        {
            var tasks = new ConcurrentQueue<Task>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Enqueue(db.Users.AnyAsync());
            }

            await Task.WhenAll(tasks);
        }

        public async Task DiscardedHashSetAdd(AppDbContext db)
        {
            // Discard binds to HashSet.Add (bool), not the EF argument.
            var tasks = new HashSet<Task>();
            foreach (var id in new[] { 1, 2 })
            {
                _ = tasks.Add(db.Users.AnyAsync());
            }

            await Task.WhenAll(tasks);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public void LoopAccumulatorProof_RequiresEmptyConstructionWithoutSeededInitializers()
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
        Assert.Contains("IsSafeAccumulatorConstruction", source, StringComparison.Ordinal);
        Assert.Contains("creation.Arguments.Length == 0", source, StringComparison.Ordinal);
        Assert.Contains("creation.Initializer.Initializers.Length == 0", source, StringComparison.Ordinal);
        Assert.Contains("CollectionExpression", source, StringComparison.Ordinal);
    }
}
