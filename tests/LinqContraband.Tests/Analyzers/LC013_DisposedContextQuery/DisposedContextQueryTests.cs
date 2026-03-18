using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC013_DisposedContextQuery.DisposedContextQueryAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC013_DisposedContextQuery;

public class DisposedContextQueryTests
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

    public class DisposableQueryFactory : IDisposable
    {
        public IQueryable<User> Users => Enumerable.Empty<User>().AsQueryable();
        public void Dispose() {}
    }
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
    public async Task DisposedContext_ReturnDbSet_ShouldTrigger()
    {
        var test = Usings + @"
class Program
{
    public IQueryable<User> GetUsers()
    {
        using var db = new DbContext();
        return {|LC013:db.Set<User>()|};
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DisposedContext_ReturnQuery_ShouldTrigger()
    {
        var test = Usings + @"
class Program
{
    public IQueryable<User> GetUsers()
    {
        using var db = new DbContext();
        return {|LC013:db.Set<User>().Where(u => u.Id > 1)|};
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DisposedContext_UsingStatement_ShouldTrigger()
    {
        var test = Usings + @"
class Program
{
    public IQueryable<User> GetUsers()
    {
        using (var db = new DbContext())
        {
            return {|LC013:db.Set<User>()|};
        }
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExternalContext_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    public IQueryable<User> GetUsers(DbContext db)
    {
        return db.Set<User>();
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MaterializedResult_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    public List<User> GetUsers()
    {
        using var db = new DbContext();
        return db.Set<User>().ToList();
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DisposedContext_ReturnLocalAlias_ShouldTrigger()
    {
        var test = Usings + @"
class Program
{
    public IQueryable<User> GetUsers()
    {
        using var db = new DbContext();
        var query = db.Set<User>().Where(u => u.Id > 1);
        return {|LC013:query|};
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DisposedContext_ReturnComposedLocalAlias_ShouldTrigger()
    {
        var test = Usings + @"
class Program
{
    public IQueryable<User> GetUsers()
    {
        using var db = new DbContext();
        var query = db.Set<User>();
        var filtered = query.Where(u => u.Id > 1);
        return {|LC013:filtered|};
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DisposedContext_ReturnAsyncEnumerableAlias_ShouldTrigger()
    {
        var test = Usings + @"
class Program
{
    public IAsyncEnumerable<User> GetUsers()
    {
        using var db = new DbContext();
        var query = db.Set<User>().Where(u => u.Id > 1);
        return {|LC013:query.AsAsyncEnumerable()|};
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MaterializedLocalAlias_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    public List<User> GetUsers()
    {
        using var db = new DbContext();
        var users = db.Set<User>().Where(u => u.Id > 1).ToList();
        return users;
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DisposedNonDbContextOrigin_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    public IQueryable<User> GetUsers()
    {
        using var factory = new DisposableQueryFactory();
        return factory.Users;
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
