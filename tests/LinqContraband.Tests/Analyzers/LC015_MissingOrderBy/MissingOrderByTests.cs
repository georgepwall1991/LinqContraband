using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<LinqContraband.Analyzers.LC015_MissingOrderBy.MissingOrderByAnalyzer>;
using System.Threading.Tasks;
using Xunit;

namespace LinqContraband.Tests.Analyzers.LC015_MissingOrderBy
{
    public class MissingOrderByTests
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
    public class User { public int Id { get; set; } public string Name { get; set; } }
    
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
}
";

        [Fact]
        public async Task Skip_WithoutOrderBy_ShouldTrigger()
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
            
            // Trigger: Skip without OrderBy
            var result = db.Users.{|LC015:Skip|}(10);
        }
    }
}" + MockNamespace;

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Fact]
        public async Task Last_WithoutOrderBy_ShouldTrigger()
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
            
            // Trigger: Last without OrderBy
            var result = db.Users.{|LC015:Last|}();
        }
    }
}" + MockNamespace;

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Fact]
        public async Task Skip_WithOrderBy_ShouldNotTrigger()
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
            
            // Valid: OrderBy present
            var result = db.Users.OrderBy(u => u.Id).Skip(10);
        }
    }
}" + MockNamespace;

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
        
        [Fact]
        public async Task Skip_OnList_ShouldNotTrigger()
        {
            var test = Usings + @"
namespace TestApp 
{
    public class Program
    {
        public void Main()
        {
            var list = new List<User>();
            
            // Valid: IEnumerable (in-memory)
            var result = list.Skip(10);
        }
    }
}" + MockNamespace;

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}