using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC013_DisposedContextQuery.DisposedContextQueryAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC013_DisposedContextQuery;

public class DisposedContextQueryEdgeCasesTests
{
    private const string Usings = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TestNamespace;
";

    private const string MockNamespace = @"
namespace TestNamespace
{
    public class User { public int Id { get; set; } }
}

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable, IAsyncDisposable
    {
        public void Dispose() {}
        public ValueTask DisposeAsync() => default;
        public DbSet<T> Set<T>() where T : class => new DbSet<T>();
    }

    public class DbSet<T> : IQueryable<T>
    {
        public Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IAsyncEnumerable<T> AsAsyncEnumerable<T>(this IQueryable<T> source) => new AsyncEnumerableAdapter<T>();
    }

    internal sealed class AsyncEnumerableAdapter<T> : IAsyncEnumerable<T>
    {
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new AsyncEnumeratorAdapter<T>();
    }

    internal sealed class AsyncEnumeratorAdapter<T> : IAsyncEnumerator<T>
    {
        public T Current => default!;
        public ValueTask DisposeAsync() => default;
        public ValueTask<bool> MoveNextAsync() => new(false);
    }
}
";

    [Fact]
    public async Task DisposedContext_ConditionalReturn_ShouldTrigger()
    {
        var test = Usings + @"
class Program
{
    public IQueryable<User> GetUsers(bool condition, IQueryable<User> other)
    {
        using var db = new DbContext();
        var query = db.Set<User>().Where(u => u.Id > 0);
        return condition ? {|LC013:query|} : other;
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DisposedContext_CoalesceReturn_ShouldTrigger()
    {
        var test = Usings + @"
class Program
{
    public IQueryable<User> GetUsers(IQueryable<User> other)
    {
        using var db = new DbContext();
        var query = db.Set<User>().Where(u => u.Id > 0);
        return other ?? {|LC013:query|};
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DisposedContext_SwitchReturn_ShouldTriggerUnsafeArmOnly()
    {
        var test = Usings + @"
class Program
{
    public IQueryable<User> GetUsers(int mode, IQueryable<User> other)
    {
        using var db = new DbContext();
        var query = db.Set<User>().Where(u => u.Id > 0);
        return mode switch
        {
            0 => other,
            _ => {|LC013:query|}
        };
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DisposedContext_AwaitUsing_ShouldTrigger()
    {
        var test = Usings + @"
class Program
{
    public async Task<IQueryable<User>> GetUsersAsync()
    {
        await using var db = new DbContext();
        return {|LC013:db.Set<User>().Where(u => u.Id > 0)|};
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DisposedContext_AwaitUsingBlock_ShouldTrigger()
    {
        // Test for IAsyncDisposable with explicit block: await using (var db = ...) { }
        var test = Usings + @"
class Program
{
    public async Task<IQueryable<User>> GetUsersAsync()
    {
        await using (var db = new DbContext())
        {
            return {|LC013:db.Set<User>()|};
        }
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DisposedContext_AwaitUsing_ReturnAsyncEnumerable_ShouldTrigger()
    {
        var test = Usings + @"
class Program
{
    public async Task<IAsyncEnumerable<User>> GetUsersAsync()
    {
        await using var db = new DbContext();
        var query = db.Set<User>().Where(u => u.Id > 0);
        return {|LC013:query.AsAsyncEnumerable()|};
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NonDisposedContext_AwaitUsing_WithMaterialization_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    public async Task<List<User>> GetUsersAsync()
    {
        await using var db = new DbContext();
        return db.Set<User>().Where(u => u.Id > 0).ToList();
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedLocalFunctionReturn_MaterializedBeforeMethodExit_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    public List<User> GetUsers()
    {
        using var db = new DbContext();

        IQueryable<User> BuildQuery()
        {
            return db.Set<User>().Where(u => u.Id > 0);
        }

        return BuildQuery().ToList();
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedLocalFunction_WithOwnedDisposedContext_ShouldTrigger()
    {
        var test = Usings + @"
class Program
{
    public List<User> GetUsers()
    {
        IQueryable<User> BuildQuery()
        {
            using var db = new DbContext();
            return {|LC013:db.Set<User>().Where(u => u.Id > 0)|};
        }

        return BuildQuery().ToList();
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedLambdaReturn_MaterializedBeforeMethodExit_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    public List<User> GetUsers()
    {
        using var db = new DbContext();
        Func<IQueryable<User>> buildQuery = () =>
        {
            return db.Set<User>().Where(u => u.Id > 0);
        };

        return buildQuery().ToList();
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MultiAssignmentFromSameDisposedContext_ShouldTrigger()
    {
        var test = Usings + @"
class Program
{
    public IQueryable<User> GetUsers(bool condition)
    {
        using var db = new DbContext();
        IQueryable<User> query;

        if (condition)
        {
            query = db.Set<User>();
        }
        else
        {
            query = db.Set<User>().Where(u => u.Id > 0);
        }

        return {|LC013:query|};
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MultiAssignmentWithMixedOrigins_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    public IQueryable<User> GetUsers(bool condition, IQueryable<User> other)
    {
        using var db = new DbContext();
        IQueryable<User> query;

        if (condition)
        {
            query = db.Set<User>();
        }
        else
        {
            query = other;
        }

        return query;
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
