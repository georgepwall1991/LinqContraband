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
using System.Threading.Tasks;
using TestNamespace;
";

    private const string MockNamespace = @"
namespace TestNamespace
{
    public class User { public int Id { get; set; } }

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
}
";

    [Fact]
    public async Task DisposedContext_ConditionalReturn_ShouldTrigger()
    {
        var test = Usings + @"
class Program
{
    public IQueryable<User> GetUsers(bool condition)
    {
        using var db = new DbContext();
        // Should trigger on both branches if possible, or at least the bad one
        return condition ? {|LC013:db.Set<User>()|} : {|LC013:db.Set<User>()|};
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
        return other ?? {|LC013:db.Set<User>()|};
    }
}" + MockNamespace;
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DisposedContext_AwaitUsing_ShouldTrigger()
    {
        // Test for IAsyncDisposable pattern: await using var db = ...;
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
    public async Task NonDisposedContext_AwaitUsing_WithMaterialization_ShouldNotTrigger()
    {
        // If we materialize inside the using block, no diagnostic
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
}
