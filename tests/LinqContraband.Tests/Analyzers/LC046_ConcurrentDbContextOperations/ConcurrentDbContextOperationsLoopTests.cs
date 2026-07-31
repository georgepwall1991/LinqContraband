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

        public void LocalDrainRunsOnlyAfterLoop(AppDbContext db)
        {
            var tasks = new List<Task<bool>>();
            void Drain() => Task.WhenAll(tasks).GetAwaiter().GetResult();

            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add({|#3:db.Users.AnyAsync()|});
            }

            Drain();
        }

        public void DelegateDrainRunsOnlyAfterLoop(AppDbContext db)
        {
            var tasks = new List<Task<bool>>();
            void Drain() =>
                Task.WhenAll(tasks).GetAwaiter().GetResult();
            Action drain = Drain;

            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add({|#4:db.Users.AnyAsync()|});
            }

            drain();
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
        var postLoopLocalDrain = VerifyCS.Diagnostic()
            .WithLocation(3)
            .WithArguments("db");
        var postLoopDelegateDrain = VerifyCS.Diagnostic()
            .WithLocation(4)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            declarationInitializer,
            separateAssignment,
            loopVariableArgument,
            postLoopLocalDrain,
            postLoopDelegateDrain);
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
    public async Task ForeachTaskList_WithNullableContextParameter_ShouldNotTrigger()
    {
        var test = @"#nullable enable
	using Microsoft.EntityFrameworkCore;
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
        public void Run(AppDbContext? db)
        {
            var tasks = new List<Task<bool>>();
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
    public async Task ForeachTaskList_WithNullGuardedContextParameter_ShouldTrigger()
    {
        var test = @"#nullable enable
using Microsoft.EntityFrameworkCore;
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
        public void Run(AppDbContext? db)
        {
            if (db is null)
                return;

            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add({|#0:db.Users.AnyAsync()|});
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
    public async Task ForeachTaskList_WithUnprovenParameterNullability_ShouldNotTrigger()
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
        public void NullForgiven(AppDbContext? db)
        {
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db!.Users.AnyAsync());
            }
        }

#nullable disable
        public void Oblivious(AppDbContext db)
        {
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.AnyAsync());
            }
        }
#nullable enable
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ForeachTaskList_WithInvalidRequiredTerminalArguments_ShouldNotTrigger()
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
        public void NullRawSql(AppDbContext db)
        {
            var tasks = new List<Task<int>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Database.ExecuteSqlRawAsync((string)null));
            }
        }

        public void NullInterpolatedSql(AppDbContext db)
        {
            var tasks = new List<Task<int>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Database.ExecuteSqlInterpolatedAsync(
                    (FormattableString)null));
            }
        }

        public void NullFindKeys(AppDbContext db)
        {
            var tasks = new List<ValueTask<User>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.FindAsync<User>((object[])null));
            }
        }

        public void EmptyQuerySql(AppDbContext db)
        {
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.FromSqlRaw("""").AnyAsync());
            }
        }

        public void EmptyFindKeys(AppDbContext db)
        {
            var keys = new object[0];
            var tasks = new List<ValueTask<User>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.FindAsync<User>(keys));
            }
        }

        public void OmittedFindKeys(AppDbContext db)
        {
            var tasks = new List<ValueTask<User>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.FindAsync<User>());
            }
        }

        public void NullRawSqlParameters(AppDbContext db)
        {
            object[] parameters = null;
            var tasks = new List<Task<int>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Database.ExecuteSqlRawAsync(
                    ""SELECT 1"",
                    parameters));
            }
        }

        public void NullQuerySqlParameters(AppDbContext db)
        {
            object[] parameters = null;
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users
                    .FromSqlRaw(""SELECT 1"", parameters)
                    .AnyAsync());
            }
        }

        public void DefinitelyCancelled(AppDbContext db)
        {
            var canceled = new System.Threading.CancellationToken(true);
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.AnyAsync(canceled));
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ForeachTaskList_WithValidRequiredTerminalArguments_ShouldTrigger()
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
        public void RawSql(AppDbContext db)
        {
            var sql = ""SELECT 1"";
            var tasks = new List<Task<int>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add({|#0:db.Database.ExecuteSqlRawAsync(sql)|});
            }
        }

        public void InterpolatedSql(AppDbContext db)
        {
            var tasks = new List<Task<int>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add({|#1:db.Database.ExecuteSqlInterpolatedAsync(
                    $""SELECT {id}"")|});
            }
        }

        public void ExpandedFindKeys(AppDbContext db)
        {
            var tasks = new List<ValueTask<User>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add({|#2:db.FindAsync<User>(id)|});
            }
        }

        public void StableFindKeys(AppDbContext db)
        {
            var keys = new object[] { 1 };
            var tasks = new List<ValueTask<User>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add({|#3:db.FindAsync<User>(keys)|});
            }
        }

        public void RawSqlParameters(AppDbContext db)
        {
            var parameters = new object[] { 1 };
            var tasks = new List<Task<int>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add({|#4:db.Database.ExecuteSqlRawAsync(
                    ""SELECT {0}"",
                    parameters)|});
            }
        }

        public void QuerySqlParameters(AppDbContext db)
        {
            var parameters = new object[] { 1 };
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add({|#5:db.Users
                    .FromSqlRaw(""SELECT {0}"", parameters)
                    .AnyAsync()|});
            }
        }

        public void NonCancelledToken(AppDbContext db)
        {
            var cancellationToken =
                new System.Threading.CancellationToken(false);
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add({|#6:db.Users.AnyAsync(cancellationToken)|});
            }
        }
    }
}";

        var rawSql = VerifyCS.Diagnostic()
            .WithLocation(0)
            .WithArguments("db");
        var interpolatedSql = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithArguments("db");
        var expandedFindKeys = VerifyCS.Diagnostic()
            .WithLocation(2)
            .WithArguments("db");
        var stableFindKeys = VerifyCS.Diagnostic()
            .WithLocation(3)
            .WithArguments("db");
        var rawSqlParameters = VerifyCS.Diagnostic()
            .WithLocation(4)
            .WithArguments("db");
        var querySqlParameters = VerifyCS.Diagnostic()
            .WithLocation(5)
            .WithArguments("db");
        var nonCancelledToken = VerifyCS.Diagnostic()
            .WithLocation(6)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            rawSql,
            interpolatedSql,
            expandedFindKeys,
            stableFindKeys,
            rawSqlParameters,
            querySqlParameters,
            nonCancelledToken);
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
        private List<Task<bool>> _escapedBool = new List<Task<bool>>();

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

        public void TransitiveLocalFunctionEscape(AppDbContext db)
        {
            var tasks = new List<Task<User>>();
            Outer();

            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.ElementAtAsync(DrainEscaped()));
            }

            void Outer() => Inner();
            void Inner() => _escaped = tasks;
        }

        public void DelegateLocalFunctionEscape(AppDbContext db)
        {
            var tasks = new List<Task<bool>>();
            void Escape() => _escapedBool = tasks;
            Action escape = Escape;
            escape();

            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.AnyAsync());
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
	using System.Linq;
	using System.Linq.Expressions;
	using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class ContextHolder
    {
        public readonly AppDbContext Field = null;
        public AppDbContext Property { get; } = null;
    }

    public sealed class ConstructorInvalidatedDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();

        public ConstructorInvalidatedDbContext() => Users = null;
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

        public void RetainedLocalFunctionClosure(AppDbContext db)
        {
            var tasks = new List<Task<bool>>();
            void Drain() =>
                Task.WhenAll(tasks).GetAwaiter().GetResult();
            Callbacks.Drain = Drain;

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

        public void ThrowingQueryReceiverArgument(AppDbContext db)
        {
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users
                    .Skip(id == 2 ? Throw() : id)
                    .AnyAsync());
            }
        }

        public void NullTransparentQueryArgument(AppDbContext db)
        {
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users
                    .Where((Expression<Func<User, bool>>)null)
                    .AnyAsync());
            }
        }

        public void NullQueryableSourceArgument(AppDbContext db)
        {
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users
                    .Concat((IQueryable<User>)null)
                    .AnyAsync());
            }
        }

        public void NullRequiredScalarQueryArgument(AppDbContext db)
        {
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users
                    .FromSqlRaw(id == 2 ? null : ""SELECT 1"")
                    .AnyAsync());
            }
        }

        public void NullTerminalSelector(AppDbContext db)
        {
            var tasks = new List<Task<Dictionary<int, User>>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.ToDictionaryAsync(
                    (Func<User, int>)null));
            }
        }

        public void NullContextField(ContextHolder holder)
        {
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(holder.Field.Users.AnyAsync());
            }
        }

        public void NullContextProperty(ContextHolder holder)
        {
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(holder.Property.Users.AnyAsync());
            }
        }

        public void NullQueryAlias(ContextHolder holder)
        {
            var users = holder.Property.Users;
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(users.AnyAsync());
            }
        }

        public void ConstructorInvalidatedQueryMember(
            ConstructorInvalidatedDbContext db)
        {
            var tasks = new List<Task<bool>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.AnyAsync());
            }
        }

        public void ThrowingExplicitArgumentConversion(AppDbContext db)
        {
            var tasks = new List<Task<User>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.Users.ElementAtAsync(
                    (int)(id == 2 ? (object)""bad"" : 0)));
            }
        }

        public void ThrowingExpandedParamsArgument(AppDbContext db)
        {
            var tasks = new List<ValueTask<User>>();
            foreach (var id in new[] { 1, 2 })
            {
                tasks.Add(db.FindAsync<User>(
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
