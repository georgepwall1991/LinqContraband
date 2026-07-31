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
    public async Task ForeachOverTwoElementArray_AddsEfTasksToStableList_ShouldTrigger()
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
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add({|#0:db.Users.AnyAsync()|});
            }

            await Task.WhenAll(tasks);
        }

        public void SeparateAssignment(AppDbContext db)
        {
            List<Task<bool>> tasks;
            tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add({|#1:db.Users.AnyAsync()|});
            }
        }

        public void LoopVariableArgument(AppDbContext db)
        {
            var tasks = new List<Task<User>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add({|#2:db.Users.ElementAtAsync(id)|});
            }
        }
    }
}";

        var declarationInitializer = VerifyCS.Diagnostic()
            .WithLocation(0)
            .WithArguments("db");
        var separateAssignment = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithArguments("db");
        var loopVariableArgument = VerifyCS.Diagnostic()
            .WithLocation(2)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            declarationInitializer,
            separateAssignment,
            loopVariableArgument);
    }

    [Fact]
    public async Task ForeachOverTwoElementArray_ConditionallyAddsEfTasksToProvenNonNullList_ShouldTrigger()
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
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks?.Add({|#0:db.Users.AnyAsync()|});
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
    public async Task ForeachOverTwoElementArray_AddsSafelyExplicitlyConvertedEfTasks_ShouldTrigger()
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
        public void Identity(AppDbContext db)
        {
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add((Task<bool>){|#0:db.Users.AnyAsync()|});
            }
        }

        public void Upcast(AppDbContext db)
        {
            var tasks = new List<Task>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add((Task){|#1:db.Users.AnyAsync()|});
            }
        }
    }
}";

        var identity = VerifyCS.Diagnostic()
            .WithLocation(0)
            .WithArguments("db");
        var upcast = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, identity, upcast);
    }

    [Fact]
    public async Task ForeachTaskListWithoutProvenRepeatedEfTasks_ShouldNotTrigger()
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

    public sealed class TaskCollector
    {
        public void Add(Task<bool> task) { }
    }

    public sealed class Program
    {
        public async Task Singleton(AppDbContext db)
        {
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1 })
            {
                tasks.Add(db.Users.AnyAsync());
            }

            await Task.WhenAll(tasks);
        }

        public async Task PerIterationContext()
        {
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                var db = new AppDbContext();
                tasks.Add(db.Users.AnyAsync());
            }

            await Task.WhenAll(tasks);
        }

        public async Task Sequential(AppDbContext db)
        {
            var results = new List<bool>();
            foreach (var id in new[] { 1, 2 })
            {
                results.Add(await db.Users.AnyAsync());
            }
        }

        public void CustomCollector(AppDbContext db)
        {
            var tasks = new TaskCollector();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.AnyAsync());
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ForeachTaskList_WithThrowingReceiver_ShouldNotTrigger()
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
        public void Run(AppDbContext db)
        {
            foreach (var id in new[] { 1, 2 })
            {
                Fail().Add(db.Users.AnyAsync());
            }
        }

        private static List<Task<bool>> Fail() =>
            throw new InvalidOperationException();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ForeachTaskList_WithCompletingUserDefinedConversion_ShouldNotTrigger()
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

    public readonly struct CompletedTask
    {
        public static implicit operator CompletedTask(Task<bool> task)
        {
            task.GetAwaiter().GetResult();
            return default;
        }
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var results = new List<CompletedTask>();
            foreach (var id in new[] { 1, 2 })
            {
                results.Add(db.Users.AnyAsync());
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ForeachTaskList_WithThrowingExplicitCast_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Collections.Generic;
	using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class Never { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var tasks = new List<Never>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add((Never)(object)db.Users.AnyAsync());
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ForeachTaskList_DrainsPreviousTasksBeforeStartingNext_ShouldNotTrigger()
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

    public sealed class DrainingItem
    {
        private readonly List<Task<bool>> _tasks;

        public DrainingItem(List<Task<bool>> tasks) => _tasks = tasks;

        public void Deconstruct(out int id, out int ignored)
        {
            Task.WhenAll(_tasks).GetAwaiter().GetResult();
            id = ignored = 0;
        }
    }

    public sealed class Program
    {
        public void Direct(AppDbContext db)
        {
            var tasks = new List<Task<User>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.ElementAtAsync(Drain(tasks)));
            }
        }

        public void StableAlias(AppDbContext db)
        {
            var tasks = new List<Task<User>>();
            var alias = tasks;
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.ElementAtAsync(Drain(alias)));
            }
        }

        public void ReassignedAlias(AppDbContext db)
        {
            var tasks = new List<Task<User>>();
            var alias = tasks;
            alias = tasks;
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.ElementAtAsync(Drain(alias)));
            }
        }

        public void CapturedLocalFunction(AppDbContext db)
        {
            var tasks = new List<Task<User>>();

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

        public void CapturedAliasAssignedAfterLocalFunctionDeclaration(
            AppDbContext db)
        {
            List<Task<User>> alias = null;

            int DrainCaptured()
            {
                Task.WhenAll(alias).GetAwaiter().GetResult();
                return 0;
            }

            var tasks = new List<Task<User>>();
            alias = tasks;
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.ElementAtAsync(DrainCaptured()));
            }
        }

        public void DeconstructionDrainsBetweenIterations(AppDbContext db)
        {
            var tasks = new List<Task<bool>>();
            foreach (var (id, ignored) in new[]
                     {
                         new DrainingItem(tasks),
                         new DrainingItem(tasks)
                     })
            {
                tasks.Add(db.Users.AnyAsync());
            }
        }

        private static int Drain(List<Task<User>> tasks)
        {
            Task.WhenAll(tasks).GetAwaiter().GetResult();
            return 0;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ForeachTaskList_EscapesBeforeLoop_ShouldNotTrigger()
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

    public sealed class Holder
    {
        public List<Task<User>> Tasks { get; set; }

        public int Drain()
        {
            Task.WhenAll(Tasks).GetAwaiter().GetResult();
            return 0;
        }
    }

    public sealed class Program
    {
        private List<Task<User>> _escaped = new List<Task<User>>();

        public void FieldEscape(AppDbContext db)
        {
            var tasks = new List<Task<User>>();
            _escaped = tasks;

            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.ElementAtAsync(DrainEscaped()));
            }
        }

        public void RetainingHelperEscape(AppDbContext db)
        {
            var tasks = new List<Task<User>>();
            Retain(tasks);

            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.ElementAtAsync(DrainEscaped()));
            }
        }

        public void AliasedFieldEscape(AppDbContext db)
        {
            var tasks = new List<Task<User>>();
            var alias = tasks;
            _escaped = alias;

            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.ElementAtAsync(DrainEscaped()));
            }
        }

        public void CoalescingPropertyEscape(AppDbContext db)
        {
            var tasks = new List<Task<User>>();
            var holder = new Holder();
            holder.Tasks ??= tasks;

            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.ElementAtAsync(holder.Drain()));
            }
        }

        public void RefLocalFieldEscape(AppDbContext db)
        {
            var tasks = new List<Task<User>>();
            ref List<Task<User>> slot = ref _escaped;
            slot = tasks;

            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.ElementAtAsync(DrainEscaped()));
            }
        }

        private void Retain(List<Task<User>> tasks) => _escaped = tasks;

        private int DrainEscaped()
        {
            Task.WhenAll(_escaped).GetAwaiter().GetResult();
            return 0;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ForeachTaskList_WithoutProvenRepeatedSafeExecution_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System;
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

    public static class Callbacks
    {
        public static Action Drain { get; set; }
    }

    public sealed class SingleItem : IEnumerable<int>
    {
        public static explicit operator SingleItem(int[] values) =>
            new SingleItem();

        public IEnumerator<int> GetEnumerator()
        {
            yield return 1;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class AsyncWrapper
    {
        private readonly int[] _values;

        private AsyncWrapper(int[] values) => _values = values;

        public static explicit operator AsyncWrapper(int[] values) =>
            new AsyncWrapper(values);

        public AsyncArrayEnumerator GetAsyncEnumerator() =>
            new AsyncArrayEnumerator(_values);
    }

    public struct AsyncArrayEnumerator
    {
        private readonly int[] _values;
        private int _index;

        public AsyncArrayEnumerator(int[] values)
        {
            _values = values;
            _index = -1;
        }

        public int Current => _values[_index];

        public async ValueTask<bool> MoveNextAsync()
        {
            await Task.Yield();
            _index++;
            return _index < _values.Length;
        }

        public ValueTask DisposeAsync() => default;
    }

    public readonly struct ThrowOnTwo
    {
        public static implicit operator ThrowOnTwo(int value) =>
            value == 2
                ? throw new InvalidOperationException()
                : default;
    }

    public static class IntDeconstructionExtensions
    {
        public static void Deconstruct(
            this int value,
            out int id,
            out int ignored)
        {
            if (value == 2)
                throw new InvalidOperationException();

            id = value;
            ignored = value;
        }
    }

    public sealed class Program
    {
        public void ThrowingSource(AppDbContext db)
        {
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { Throw(), 2 })
            {
                tasks.Add(db.Users.AnyAsync());
            }
        }

        public void SingletonConversion(AppDbContext db)
        {
            var tasks = new List<Task<bool>>();
            foreach (var id in (SingleItem)new[] { 1, 2 })
            {
                tasks.Add(db.Users.AnyAsync());
            }
        }

        public async Task AsyncEnumeration(AppDbContext db)
        {
            var tasks = new List<Task<bool>>();
            await foreach (var id in (AsyncWrapper)new[] { 1, 2 })
            {
                tasks.Add(db.Users.AnyAsync());
            }
        }

        public void RetainedClosure(AppDbContext db)
        {
            var tasks = new List<Task<bool>>();
            Callbacks.Drain = () =>
                Task.WhenAll(tasks).GetAwaiter().GetResult();

            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.AnyAsync());
            }
        }

        public void ThrowingIterationVariableConversion(AppDbContext db)
        {
            var tasks = new List<Task<bool>>();
            foreach (ThrowOnTwo id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.AnyAsync());
            }
        }

        public void ThrowingDeconstruction(AppDbContext db)
        {
            var tasks = new List<Task<bool>>();
            foreach (var (id, ignored) in new[] { 1, 2 })
            {
                tasks.Add(db.Users.AnyAsync());
            }
        }

        public void ThrowingInvocationArgument(AppDbContext db)
        {
            var tasks = new List<Task<User>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.ElementAtAsync(
                    id == 2 ? Throw() : id));
            }
        }

        private static int Throw() =>
            throw new InvalidOperationException();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
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
