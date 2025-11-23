using System.Threading.Tasks;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<LinqContraband.Analyzers.LC013_DisposedContextQuery.DisposedContextQueryAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC013_DisposedContextQuery
{
    public class DisposedContextQueryTests
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
    
    public class DbContext : IDisposable
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
    }
}
