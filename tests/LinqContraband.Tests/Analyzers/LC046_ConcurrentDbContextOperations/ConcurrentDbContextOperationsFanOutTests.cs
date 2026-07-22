using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC046_ConcurrentDbContextOperations.ConcurrentDbContextOperationsAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC046_ConcurrentDbContextOperations;

public sealed class ConcurrentDbContextOperationsFanOutTests
{
    private const string EfMock = ConcurrentDbContextOperationsTests.EfMock;

    [Fact]
    public async Task TaskWhenAll_SelectCapturingOuterContext_ShouldTriggerAtFanOut()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, IEnumerable<int> ids)
        {
            await {|#1:Task.WhenAll(ids.Select(id => {|#0:db.Users.AnyAsync()|}))|};
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
    public async Task TaskWhenAll_ConditionalSelectorExecutesEfCallOnce_ShouldNotTrigger()
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
        public async Task Run(AppDbContext db)
        {
            await Task.WhenAll(
                new[] { 0, 1 }.Select(index =>
                    index == 0 ? db.Users.AnyAsync() : Task.CompletedTask));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_CoalescingSelectorMaySkipEfCall_ShouldNotTrigger()
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
        public async Task Run(AppDbContext db)
        {
            Task<bool> cached = Task.FromResult(true);
            await Task.WhenAll(
                new[] { 0, 1 }.Select(_ => cached ?? db.Users.AnyAsync()));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_SelectOverEnumerableEmpty_ShouldNotTrigger()
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
        public async Task Run(AppDbContext db)
        {
            await Task.WhenAll(
                Enumerable.Empty<int>().Select(_ => db.Users.AnyAsync()));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_SelectOverEnumerableRepeatOne_ShouldNotTrigger()
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
        public async Task Run(AppDbContext db)
        {
            await Task.WhenAll(
                Enumerable.Repeat(42, 1).Select(_ => db.Users.AnyAsync()));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_SelectOverEnumerableRepeatTwo_ShouldTriggerAtFanOut()
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
        public async Task Run(AppDbContext db)
        {
            await {|#1:Task.WhenAll(
                Enumerable.Repeat(42, 2).Select(_ => {|#0:db.Users.AnyAsync()|}))|};
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
    public async Task TaskWhenAll_SelectOverFixedSizeSingletonArray_ShouldNotTrigger()
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
        public async Task Run(AppDbContext db)
        {
            await Task.WhenAll(
                new int[1].Select(_ => db.Users.AnyAsync()));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_SelectOverStableSingletonArrayLocal_ShouldNotTrigger()
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
        public async Task Run(AppDbContext db)
        {
            var ids = new[] { 1 };
            await Task.WhenAll(ids.Select(_ => db.Users.AnyAsync()));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_SelectOverReassignedSingletonArrayLocal_ShouldStillTriggerAtFanOut()
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
        public async Task Run(AppDbContext db)
        {
            var ids = new[] { 1 };
            ids = new[] { 1, 2 };
            await {|#1:Task.WhenAll(ids.Select(_ => {|#0:db.Users.AnyAsync()|}))|};
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
    public async Task TaskWhenAll_UserDefinedConversionFromSingletonArray_ShouldStillTriggerAtFanOut()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Many : IEnumerable<int>
    {
        public static implicit operator Many(int[] source) => new Many();

        public IEnumerator<int> GetEnumerator()
        {
            yield return 1;
            yield return 2;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            Many ids = new[] { 1 };
            await {|#1:Task.WhenAll(ids.Select(_ => {|#0:db.Users.AnyAsync()|}))|};
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
    public async Task TaskWhenAll_SelectOverIntegralSingletonArrayBounds_ShouldNotTrigger()
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
        public async Task Run(AppDbContext db)
        {
            await Task.WhenAll(new int[1U].Select(_ => db.Users.AnyAsync()));
            await Task.WhenAll(new int[1L].Select(_ => db.Users.AnyAsync()));
            await Task.WhenAll(new int[1UL].Select(_ => db.Users.AnyAsync()));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_SelectOverFixedSizeTwoElementArray_ShouldTriggerAtFanOut()
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
        public async Task Run(AppDbContext db)
        {
            await {|#1:Task.WhenAll(
                new int[2].Select(_ => {|#0:db.Users.AnyAsync()|}))|};
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
    public async Task TaskWhenAll_ReorderedNamedStaticSelect_ShouldTriggerAtFanOut()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
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
        public async Task Run(AppDbContext db, IEnumerable<int> ids)
        {
            await {|#1:Task.WhenAll(Enumerable.Select(
                selector: id => {|#0:db.Users.AnyAsync()|},
                source: ids))|};
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
    public async Task TaskWhenAll_SelectCreatingContextPerItem_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public interface IFactory
    {
        AppDbContext Create();
    }

    public sealed class Program
    {
        public async Task Run(IFactory factory, IEnumerable<int> ids)
        {
            await Task.WhenAll(ids.Select(id => factory.Create().Users.AnyAsync()));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_SelectWithUnexecutedNestedLambda_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
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
        public async Task Run(AppDbContext db, IEnumerable<int> ids)
        {
            await Task.WhenAll(ids.Select(id =>
            {
                Func<Task<bool>> deferred = () => db.Users.AnyAsync();
                _ = deferred;
                return Task.CompletedTask;
            }));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_SelectWithUnexecutedLocalFunction_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
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
        public async Task Run(AppDbContext db, IEnumerable<int> ids)
        {
            await Task.WhenAll(ids.Select(id =>
            {
                Task<bool> DeferredAsync() => db.Users.AnyAsync();
                _ = (Func<Task<bool>>)DeferredAsync;
                return Task.CompletedTask;
            }));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_BlockBodiedSelectCapturingOuterContext_ShouldTriggerAtFanOut()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
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
        public async Task Run(AppDbContext db, IEnumerable<int> ids)
        {
            await {|#1:Task.WhenAll(ids.Select(id =>
            {
                return {|#0:db.Users.AnyAsync()|};
            }))|};
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
    public async Task TaskWhenAll_SelectorSynchronousCompletion_DistinguishesActiveTasks()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public delegate void RefReplace(ref Task<bool> task);

    public sealed class CallbackInvoker
    {
        public CallbackInvoker(Task<bool> active, Action replace) => replace();
    }

    public sealed class CallbackSetter
    {
        public Action Callback
        {
            get => null!;
            set => value();
        }
    }

    public static class CallbackExtensions
    {
        public static void Execute(this Action callback) => callback();
    }

    public sealed class Program
    {
        public async Task Wait(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                task.Wait();
                return Task.CompletedTask;
            }));
        }

        public async Task GetResult(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                task.GetAwaiter().GetResult();
                return Task.CompletedTask;
            }));
        }

        public async Task LaterOperationStillRunsConcurrently(AppDbContext db)
        {
            await {|#1:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                task.Wait();
                return {|#0:db.Users.ToListAsync()|};
            }))|};
        }

        public async Task TimedWaitDoesNotProveCompletion(AppDbContext db)
        {
            await {|#3:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#2:db.Users.AnyAsync()|};
                task.Wait(0);
                return task;
            }))|};
        }

        public async Task CompletingDifferentTaskDoesNotProveCompletion(AppDbContext db)
        {
            await {|#5:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#4:db.Users.AnyAsync()|};
                Task.CompletedTask.Wait();
                return task;
            }))|};
        }

        public async Task StableAliasCompletion(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                var alias = task;
                alias.Wait();
                return Task.CompletedTask;
            }));
        }

        public async Task StandaloneAssignmentCompletion(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                Task<bool> task;
                task = db.Users.AnyAsync();
                task.Wait();
                return Task.CompletedTask;
            }));
        }

        public async Task StandaloneAliasCompletion(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                Task<bool> alias;
                alias = task;
                alias.Wait();
                return Task.CompletedTask;
            }));
        }

        public async Task CompletionBeforeReassignment(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                task.Wait();
                task = Task.FromResult(true);
                return task;
            }));
        }

        public async Task CompletionBeforeRefReplacement(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                task.Wait();
                Replace(ref task);
                return Task.CompletedTask;
            }));
        }

        public async Task CompletionBeforeDynamicRefReplacement(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                task.Wait();
                dynamic replace = (RefReplace)Replace;
                replace(ref task);
                return Task.CompletedTask;
            }));
        }

        public async Task DynamicRefReplacementDoesNotProveCompletion(AppDbContext db)
        {
            await {|#7:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#6:db.Users.AnyAsync()|};
                dynamic replace = (RefReplace)Replace;
                replace(ref task);
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task NestedDynamicRefReplacementDoesNotProveCompletion(AppDbContext db)
        {
            await {|#9:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#8:db.Users.AnyAsync()|};
                void Swap()
                {
                    dynamic replace = (RefReplace)Replace;
                    replace(ref task);
                }

                Swap();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task InlineCallbackReplacementDoesNotProveCompletion(AppDbContext db)
        {
            await {|#11:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                Task<bool> task = Task.FromResult(true);
                Invoke(
                    task = {|#10:db.Users.AnyAsync()|},
                    () => task = Task.FromResult(true));
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task ConstructorCallbackReplacementDoesNotProveCompletion(AppDbContext db)
        {
            await {|#13:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                Task<bool> task = Task.FromResult(true);
                CallbackInvoker invoker = new CallbackInvoker(
                    task = {|#12:db.Users.AnyAsync()|},
                    () => task = Task.FromResult(true));
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task LaterConstructorCallbackReplacementDoesNotProveCompletion(AppDbContext db)
        {
            await {|#15:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#14:db.Users.AnyAsync()|};
                CallbackInvoker invoker = new CallbackInvoker(
                    task,
                    () => task = Task.FromResult(true));
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task PropertySetterCallbackReplacementDoesNotProveCompletion(AppDbContext db)
        {
            await {|#17:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var setter = new CallbackSetter();
                var task = {|#16:db.Users.AnyAsync()|};
                setter.Callback = () => task = Task.FromResult(true);
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task CoalescingPropertySetterCallbackReplacementDoesNotProveCompletion(AppDbContext db)
        {
            await {|#19:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var setter = new CallbackSetter();
                var task = {|#18:db.Users.AnyAsync()|};
                setter.Callback ??= () => task = Task.FromResult(true);
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task TransitiveLocalFunctionReplacementDoesNotProveCompletion(AppDbContext db)
        {
            await {|#21:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#20:db.Users.AnyAsync()|};
                void Inner() => task = Task.FromResult(true);
                void Outer() => Inner();
                Outer();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task MethodGroupCallbackReplacementDoesNotProveCompletion(AppDbContext db)
        {
            await {|#23:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#22:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Invoke(task, ReplaceTask);
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task ExtensionCallbackReplacementDoesNotProveCompletion(AppDbContext db)
        {
            await {|#25:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#24:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action replace = ReplaceTask;
                replace.Execute();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task UnknownCallbackSinkDoesNotProveCompletion(AppDbContext db)
        {
            await {|#27:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#26:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Task.Run((Action)ReplaceTask);
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task ConditionalCallbackReplacementDoesNotProveCompletion(AppDbContext db)
        {
            await {|#29:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#28:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                if (DateTime.UtcNow.Ticks > 0)
                    callback = () => { };
                callback();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task AliasBeforeCallbackReplacementDoesNotProveCompletion(AppDbContext db)
        {
            await {|#31:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#30:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                var alias = callback;
                callback = () => { };
                alias();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task DirectStoredCallbackDoesNotProveCompletion(AppDbContext db)
        {
            await {|#33:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#32:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                callback();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task NestedAliasCaptureBeforeReplacementDoesNotProveCompletion(AppDbContext db)
        {
            await {|#35:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#34:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action alias = () => { };
                void Capture() => alias = callback;
                Capture();
                callback = () => { };
                alias();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task ConditionalNestedAliasCaptureDoesNotProveCompletion(AppDbContext db)
        {
            await {|#37:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#36:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action alias = () => { };
                void Capture() => alias = callback;
                if (DateTime.UtcNow.Ticks > 0)
                    Capture();
                callback = () => { };
                alias();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task NestedBindingAndInvocationDoesNotProveCompletion(AppDbContext db)
        {
            await {|#39:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#38:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                void Run()
                {
                    Action callback = ReplaceTask;
                    callback();
                }

                Run();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task CombinedCallbackPreservesOriginalReplacement(AppDbContext db)
        {
            await {|#41:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#40:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                callback += () => { };
                callback();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task NestedCombinedCallbackPreservesOriginalReplacement(AppDbContext db)
        {
            await {|#43:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#42:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                void Run()
                {
                    Action callback = ReplaceTask;
                    callback += () => { };
                    callback();
                }

                Run();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task NestedUnrelatedCallbackRemovalPreservesOriginalReplacement(AppDbContext db)
        {
            await {|#45:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#44:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                void Run()
                {
                    Action callback = ReplaceTask;
                    callback -= () => { };
                    callback();
                }

                Run();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task CallbackAddedToDifferentStoragePreservesReplacement(AppDbContext db)
        {
            await {|#47:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#46:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action combined = () => { };
                combined += callback;
                combined();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task CallbackAddedThenExactlyRemovedPreservesCompletion(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action combined = () => { };
                combined += callback;
                combined -= callback;
                combined();
                task.Wait();
                return Task.CompletedTask;
            }));
        }

        public async Task NestedCallbackAddedThenExactlyRemovedPreservesCompletion(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                void Run()
                {
                    Action combined = () => { };
                    combined += callback;
                    combined -= callback;
                    combined();
                }

                Run();
                task.Wait();
                return Task.CompletedTask;
            }));
        }

        public async Task ReboundRemovalOperandDoesNotRemoveAddedCallback(AppDbContext db)
        {
            await {|#49:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#48:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action combined = () => { };
                combined += callback;
                callback = () => { };
                combined -= callback;
                combined();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task DuplicateCallbackAdditionSurvivesSingleRemoval(AppDbContext db)
        {
            await {|#51:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#50:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action combined = () => { };
                combined += callback;
                combined += callback;
                combined -= callback;
                combined();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task ConditionalDuplicateCallbackAdditionMaySurviveRemoval(AppDbContext db)
        {
            await {|#53:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#52:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action combined = () => { };
                combined += callback;
                if (DateTime.UtcNow.Ticks > 0)
                    combined += callback;
                combined -= callback;
                combined();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task ConditionalRemovalOperandRebindingMayPreserveAddedCallback(AppDbContext db)
        {
            await {|#55:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#54:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action combined = () => { };
                combined += callback;
                if (DateTime.UtcNow.Ticks > 0)
                    callback = () => { };
                combined -= callback;
                combined();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task NestedCallbackAdditionAfterRemovalRestoresReplacement(AppDbContext db)
        {
            await {|#57:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#56:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action combined = () => { };
                void AddCallback() => combined += callback;
                AddCallback();
                combined -= callback;
                AddCallback();
                combined();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task RepeatedNestedCallbackAdditionsSurviveFewerRemovals(AppDbContext db)
        {
            await {|#59:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#58:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action combined = () => { };
                void AddCallback() => combined += callback;
                AddCallback();
                AddCallback();
                AddCallback();
                combined -= callback;
                combined -= callback;
                combined();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task CompoundIncrementLoopAdditionsSurviveFewerRemovals(AppDbContext db)
        {
            await {|#61:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#60:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action combined = () => { };
                for (var i = 0; i < 3; i += 1)
                    combined += callback;
                combined -= callback;
                combined -= callback;
                combined();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task LoopedNestedCallbackInvocationsSurviveFewerRemovals(AppDbContext db)
        {
            await {|#63:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#62:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action combined = () => { };
                void AddCallback() => combined += callback;
                for (var i = 0; i < 3; i++)
                    AddCallback();
                combined -= callback;
                combined -= callback;
                combined();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task TransitivelyLoopedCallbackInvocationsSurviveFewerRemovals(AppDbContext db)
        {
            await {|#65:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#64:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action combined = () => { };
                void AddInner() => combined += callback;
                void AddOuter() => AddInner();
                for (var i = 0; i < 3; i++)
                    AddOuter();
                combined -= callback;
                combined -= callback;
                combined();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task UninvokedLocalFunctionMethodGroupPreservesCompletion(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                void Inner() => task = Task.FromResult(true);
                void Outer() => Inner();
                Action unused = Outer;
                _ = unused.GetHashCode();
                task.Wait();
                return Task.CompletedTask;
            }));
        }

        public async Task NonExecutingCallbackArgumentPreservesCompletion(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                void ReplaceTask() => task = Task.FromResult(true);
                Observe(ReplaceTask);
                task.Wait();
                return Task.CompletedTask;
            }));
        }

        public async Task StringConcatenationDoesNotStoreExecutableCallback(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                var text = """";
                text += callback;
                Console.WriteLine(text);
                task.Wait();
                return Task.CompletedTask;
            }));
        }

        public async Task StableDelegateAliasCanRemoveCallback(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action alias = callback;
                Action combined = () => { };
                combined += callback;
                combined -= alias;
                combined();
                task.Wait();
                return Task.CompletedTask;
            }));
        }

        public async Task ReassignedAliasBeforeNestedRemovalLeavesCallbackActive(AppDbContext db)
        {
            await {|#67:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#66:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action alias = callback;
                Action combined = () => { };
                void RemoveAlias() => combined -= alias;
                combined += callback;
                alias = () => { };
                RemoveAlias();
                combined();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task ReassignedAliasBeforeLambdaRemovalLeavesCallbackActive(AppDbContext db)
        {
            await {|#69:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#68:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action alias = callback;
                Action combined = () => { };
                Action removeAlias = () => combined -= alias;
                combined += callback;
                alias = () => { };
                removeAlias();
                combined();
                task.Wait();
                return Task.CompletedTask;
            }))|};
        }

        public async Task StableAliasRemovedInsideLambdaCompletesOriginalTask(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action alias = callback;
                Action combined = () => { };
                Action removeAlias = () => combined -= alias;
                combined += callback;
                removeAlias();
                combined();
                task.Wait();
                return Task.CompletedTask;
            }));
        }

        public async Task StableAliasRemovedInsideLambdaThroughSourceVisibleInvokerCompletesOriginalTask(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action alias = callback;
                Action combined = () => { };
                Action removeAlias = () => combined -= alias;
                combined += callback;
                Invoke(task, removeAlias);
                combined();
                task.Wait();
                return Task.CompletedTask;
            }));
        }

        public async Task ReassignedCallbackPreservesCompletion(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                callback = () => { };
                callback();
                task.Wait();
                return Task.CompletedTask;
            }));
        }

        public async Task ReassignedCallbackBeforeLocalFunctionInvocationPreservesCompletion(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                void RunCallback() => callback();
                callback = () => { };
                RunCallback();
                task.Wait();
                return Task.CompletedTask;
            }));
        }

        public async Task ReassignedCallbackBeforeAnonymousRunnerPreservesCompletion(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action runner = () => callback();
                callback = () => { };
                Invoke(task, runner);
                task.Wait();
                return Task.CompletedTask;
            }));
        }

        public async Task NestedBindingReassignedBeforeInvocationPreservesCompletion(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                void ReplaceTask() => task = Task.FromResult(true);
                void Run()
                {
                    Action callback = ReplaceTask;
                    callback = () => { };
                    callback();
                }

                Run();
                task.Wait();
                return Task.CompletedTask;
            }));
        }

        private static void Replace(ref Task<bool> task) => task = Task.FromResult(true);
        private static void Invoke(Task<bool> active, Action replace) => replace();
        private static void Observe(Action callback) { }
    }
}";

        var laterOperation = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        var timedWait = VerifyCS.Diagnostic()
            .WithLocation(3)
            .WithLocation(2)
            .WithArguments("db");

        var differentTask = VerifyCS.Diagnostic()
            .WithLocation(5)
            .WithLocation(4)
            .WithArguments("db");

        var dynamicReplacement = VerifyCS.Diagnostic()
            .WithLocation(7)
            .WithLocation(6)
            .WithArguments("db");

        var nestedDynamicReplacement = VerifyCS.Diagnostic()
            .WithLocation(9)
            .WithLocation(8)
            .WithArguments("db");

        var inlineCallbackReplacement = VerifyCS.Diagnostic()
            .WithLocation(11)
            .WithLocation(10)
            .WithArguments("db");

        var constructorCallbackReplacement = VerifyCS.Diagnostic()
            .WithLocation(13)
            .WithLocation(12)
            .WithArguments("db");

        var laterConstructorCallbackReplacement = VerifyCS.Diagnostic()
            .WithLocation(15)
            .WithLocation(14)
            .WithArguments("db");

        var propertySetterCallbackReplacement = VerifyCS.Diagnostic()
            .WithLocation(17)
            .WithLocation(16)
            .WithArguments("db");

        var coalescingPropertySetterCallbackReplacement = VerifyCS.Diagnostic()
            .WithLocation(19)
            .WithLocation(18)
            .WithArguments("db");

        var transitiveLocalFunctionReplacement = VerifyCS.Diagnostic()
            .WithLocation(21)
            .WithLocation(20)
            .WithArguments("db");

        var methodGroupCallbackReplacement = VerifyCS.Diagnostic()
            .WithLocation(23)
            .WithLocation(22)
            .WithArguments("db");

        var extensionCallbackReplacement = VerifyCS.Diagnostic()
            .WithLocation(25)
            .WithLocation(24)
            .WithArguments("db");

        var unknownCallbackSink = VerifyCS.Diagnostic()
            .WithLocation(27)
            .WithLocation(26)
            .WithArguments("db");

        var conditionalCallbackReplacement = VerifyCS.Diagnostic()
            .WithLocation(29)
            .WithLocation(28)
            .WithArguments("db");

        var aliasBeforeCallbackReplacement = VerifyCS.Diagnostic()
            .WithLocation(31)
            .WithLocation(30)
            .WithArguments("db");

        var directStoredCallback = VerifyCS.Diagnostic()
            .WithLocation(33)
            .WithLocation(32)
            .WithArguments("db");

        var nestedAliasCaptureBeforeReplacement = VerifyCS.Diagnostic()
            .WithLocation(35)
            .WithLocation(34)
            .WithArguments("db");

        var conditionalNestedAliasCapture = VerifyCS.Diagnostic()
            .WithLocation(37)
            .WithLocation(36)
            .WithArguments("db");

        var nestedBindingAndInvocation = VerifyCS.Diagnostic()
            .WithLocation(39)
            .WithLocation(38)
            .WithArguments("db");

        var combinedCallback = VerifyCS.Diagnostic()
            .WithLocation(41)
            .WithLocation(40)
            .WithArguments("db");

        var nestedCombinedCallback = VerifyCS.Diagnostic()
            .WithLocation(43)
            .WithLocation(42)
            .WithArguments("db");

        var nestedUnrelatedCallbackRemoval = VerifyCS.Diagnostic()
            .WithLocation(45)
            .WithLocation(44)
            .WithArguments("db");

        var callbackAddedToDifferentStorage = VerifyCS.Diagnostic()
            .WithLocation(47)
            .WithLocation(46)
            .WithArguments("db");

        var reboundRemovalOperand = VerifyCS.Diagnostic()
            .WithLocation(49)
            .WithLocation(48)
            .WithArguments("db");

        var duplicateCallbackAddition = VerifyCS.Diagnostic()
            .WithLocation(51)
            .WithLocation(50)
            .WithArguments("db");

        var conditionalDuplicateCallbackAddition = VerifyCS.Diagnostic()
            .WithLocation(53)
            .WithLocation(52)
            .WithArguments("db");

        var conditionalRemovalOperandRebinding = VerifyCS.Diagnostic()
            .WithLocation(55)
            .WithLocation(54)
            .WithArguments("db");

        var nestedCallbackAdditionAfterRemoval = VerifyCS.Diagnostic()
            .WithLocation(57)
            .WithLocation(56)
            .WithArguments("db");

        var repeatedNestedCallbackAdditions = VerifyCS.Diagnostic()
            .WithLocation(59)
            .WithLocation(58)
            .WithArguments("db");

        var compoundIncrementLoopAdditions = VerifyCS.Diagnostic()
            .WithLocation(61)
            .WithLocation(60)
            .WithArguments("db");

        var loopedNestedCallbackInvocations = VerifyCS.Diagnostic()
            .WithLocation(63)
            .WithLocation(62)
            .WithArguments("db");

        var transitivelyLoopedCallbackInvocations = VerifyCS.Diagnostic()
            .WithLocation(65)
            .WithLocation(64)
            .WithArguments("db");

        var reassignedAliasBeforeNestedRemoval = VerifyCS.Diagnostic()
            .WithLocation(67)
            .WithLocation(66)
            .WithArguments("db");

        var reassignedAliasBeforeLambdaRemoval = VerifyCS.Diagnostic()
            .WithLocation(69)
            .WithLocation(68)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            laterOperation,
            timedWait,
            differentTask,
            dynamicReplacement,
            nestedDynamicReplacement,
            inlineCallbackReplacement,
            constructorCallbackReplacement,
            laterConstructorCallbackReplacement,
            propertySetterCallbackReplacement,
            coalescingPropertySetterCallbackReplacement,
            transitiveLocalFunctionReplacement,
            methodGroupCallbackReplacement,
            extensionCallbackReplacement,
            unknownCallbackSink,
            conditionalCallbackReplacement,
            aliasBeforeCallbackReplacement,
            directStoredCallback,
            nestedAliasCaptureBeforeReplacement,
            conditionalNestedAliasCapture,
            nestedBindingAndInvocation,
            combinedCallback,
            nestedCombinedCallback,
            nestedUnrelatedCallbackRemoval,
            callbackAddedToDifferentStorage,
            reboundRemovalOperand,
            duplicateCallbackAddition,
            conditionalDuplicateCallbackAddition,
            conditionalRemovalOperandRebinding,
            nestedCallbackAdditionAfterRemoval,
            repeatedNestedCallbackAdditions,
            compoundIncrementLoopAdditions,
            loopedNestedCallbackInvocations,
            transitivelyLoopedCallbackInvocations,
            reassignedAliasBeforeNestedRemoval,
            reassignedAliasBeforeLambdaRemoval);
    }

    [Fact]
    public async Task TaskWhenAll_SelectWithPerIterationContextProperty_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
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
        public AppDbContext Context { get; } = new AppDbContext();
    }

    public sealed class Program
    {
        public async Task Run(IEnumerable<int> ids)
        {
            await Task.WhenAll(ids.Select(id =>
            {
                var holder = new Holder();
                return holder.Context.Users.AnyAsync();
            }));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_SelectOverTakeOne_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
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
        public async Task Run(AppDbContext db, IEnumerable<int> ids)
        {
            await Task.WhenAll(ids.Take(1).Select(id => db.Users.AnyAsync()));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_SelectOverReorderedNamedStaticTakeOne_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
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
        public async Task Run(AppDbContext db, IEnumerable<int> ids)
        {
            await Task.WhenAll(Enumerable.Select(
                source: Enumerable.Take(count: 1, source: ids),
                selector: id => db.Users.AnyAsync()));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_WithOneEfTaskAndDelay_ShouldNotTrigger()
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
            await Task.WhenAll(db.Users.AnyAsync(), Task.Delay(1));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_ShortCircuitSelectorMaySkipEfCall_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
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
        public async Task Run(AppDbContext db, IEnumerable<int> ids, bool flag)
        {
            await Task.WhenAll(ids.Select(id =>
                Task.FromResult(flag && db.Users.AnyAsync().IsCompleted)));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_CoalesceAssignmentSelectorCachesEfCall_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
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
        public async Task Run(AppDbContext db, IEnumerable<int> ids)
        {
            Task<bool> cached = null;
            await Task.WhenAll(ids.Select(id =>
                cached ??= db.Users.AnyAsync()));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_SelectorIfConditionStartsEfCall_ShouldTriggerAtFanOut()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
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
        public async Task Run(AppDbContext db, IEnumerable<int> ids)
        {
            await {|#1:Task.WhenAll(ids.Select(id =>
            {
                if ({|#0:db.Users.AnyAsync()|}.IsCompleted)
                    return Task.CompletedTask;

                return Task.CompletedTask;
            }))|};
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
    public async Task AnonymousRemovalDoesNotBorrowInvocationFromSameNamedSiblingLocal()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
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
        public async Task Run(AppDbContext db)
        {
            await {|#1:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#0:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action combined = () => { };
                {
                    Action removeAlias = () => combined -= callback;
                }

                combined += callback;
                {
                    Action removeAlias = () => { };
                    removeAlias();
                }

                combined();
                task.Wait();
                return Task.CompletedTask;
            }))|};
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
    public async Task CallbackAddedOnceBeforeImmediateBreakCanBeExactlyRemoved()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
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
        public async Task Run(AppDbContext db)
        {
            await Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = db.Users.AnyAsync();
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action combined = () => { };
                for (var index = 0; index < 3; index++)
                {
                    combined += callback;
                    break;
                }

                combined -= callback;
                combined();
                task.Wait();
                return Task.CompletedTask;
            }));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ContinueCanBypassFollowingBreakAndPreserveCallback()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
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
        public async Task Run(AppDbContext db)
        {
            await {|#1:Task.WhenAll(new[] { 1, 2 }.Select(_ =>
            {
                var task = {|#0:db.Users.AnyAsync()|};
                void ReplaceTask() => task = Task.FromResult(true);
                Action callback = ReplaceTask;
                Action combined = () => { };
                for (var index = 0; index < 3; index++)
                {
                    if (index < 2)
                    {
                        combined += callback;
                        continue;
                    }

                    break;
                }

                combined -= callback;
                combined();
                task.Wait();
                return Task.CompletedTask;
            }))|};
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
    public async Task TaskRun_CapturedContextRemainsOwnedByLC036()
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
        public Task<bool> Run(AppDbContext db)
        {
            return Task.Run(() => db.Users.AnyAsync());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
