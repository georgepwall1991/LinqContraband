using System.Threading.Tasks;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<LinqContraband.Analyzers.LC004_IQueryableLeak.IQueryableLeakAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC004_IQueryableLeak
{
    public class IQueryableLeakTests
    {
        private const string Usings = @"
using System;
using System.Collections.Generic;
using System.Linq;
using TestNamespace;
";

        private const string MockNamespace = @"
namespace TestNamespace
{
    public class User { public int Id { get; set; } }
    
    // Mock EF Core classes
    public class DbContext : IDisposable 
    { 
        public void Dispose() {}
    }
    
    public class DbSet<T> : IQueryable<T>
    {
        public Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }

    namespace Microsoft.EntityFrameworkCore
    {
        // Just to allow 'using Microsoft.EntityFrameworkCore' if needed, 
        // but we are using TestNamespace.DbContext
    }
}
";

        [Fact]
        public async Task Leak_WhenPassingIQueryableToIEnumerableMethod_ShouldTrigger()
        {
            var test = Usings + @"
namespace TestApp 
{
    public class AppDbContext : DbContext { public DbSet<User> Users { get; set; } }

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var query = db.Users.Where(u => u.Id > 10);
            
            // Trigger: passing IQueryable to IEnumerable parameter
            ProcessUsers({|LC004:query|});
        }

        public void ProcessUsers(IEnumerable<User> users)
        {
            // This forces iteration or cannot be composed
            foreach(var u in users) { }
        }
    }
}" + MockNamespace;

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Fact]
        public async Task NoLeak_WhenPassingToList_ShouldNotTrigger()
        {
            var test = Usings + @"
namespace TestApp 
{
    public class AppDbContext : DbContext { public DbSet<User> Users { get; set; } }

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            
            // Safe: Explicit materialization
            ProcessUsers(db.Users.Where(u => u.Id > 10).ToList());
        }

        public void ProcessUsers(IEnumerable<User> users)
        {
            foreach(var u in users) { }
        }
    }
}" + MockNamespace;

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Fact]
        public async Task NoLeak_WhenPassingToIQueryableMethod_ShouldNotTrigger()
        {
            var test = Usings + @"
namespace TestApp 
{
    public class AppDbContext : DbContext { public DbSet<User> Users { get; set; } }

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var query = db.Users.Where(u => u.Id > 10);
            
            // Safe: Target accepts IQueryable
            ProcessUsersQuery(query);
        }

        public void ProcessUsersQuery(IQueryable<User> users)
        {
            var list = users.ToList();
        }
    }
}" + MockNamespace;

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
