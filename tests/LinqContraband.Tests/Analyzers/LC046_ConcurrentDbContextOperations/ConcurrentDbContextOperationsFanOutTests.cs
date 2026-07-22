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

        private static void Replace(ref Task<bool> task) => task = Task.FromResult(true);
        private static void Invoke(Task<bool> active, Action replace) => replace();
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
            extensionCallbackReplacement);
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
