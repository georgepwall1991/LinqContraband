using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC015_MissingOrderBy.MissingOrderByAnalyzer>;
using LinqContraband.Analyzers.LC015_MissingOrderBy;

namespace LinqContraband.Tests.Analyzers.LC015_MissingOrderBy;

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
            var result = db.Users.Skip(10);
        }
    }
}" + MockNamespace;

        var expected = VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule)
            .WithLocation(18, 35)
            .WithArguments("Skip");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
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
            var result = db.Users.Last();
        }
    }
}" + MockNamespace;

        var expected = VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule)
            .WithLocation(18, 35)
            .WithArguments("Last");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
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

    [Fact]
    public async Task OrderBy_AfterSkip_ShouldTrigger()
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
            
            // Trigger: OrderBy after Skip
            var result = db.Users.Skip(10).OrderBy(u => u.Name);
        }
    }
}" + MockNamespace;

        var expected = VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule)
            .WithLocation(18, 35)
            .WithArguments("Skip");

        var expected2 = VerifyCS.Diagnostic(MissingOrderByAnalyzer.MisplacedRule)
            .WithLocation(18, 44)
            .WithArguments("OrderBy");

        await VerifyCS.VerifyAnalyzerAsync(test, expected, expected2);
    }

    [Fact]
    public async Task OrderBy_AfterTake_ShouldTrigger()
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
            
            // Trigger: OrderBy after Take
            var result = db.Users.Take(5).OrderBy(u => u.Name);
        }
    }
}" + MockNamespace;

        var expected = VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule)
            .WithLocation(18, 35)
            .WithArguments("Take");

        var expected2 = VerifyCS.Diagnostic(MissingOrderByAnalyzer.MisplacedRule)
            .WithLocation(18, 43)
            .WithArguments("OrderBy");

        await VerifyCS.VerifyAnalyzerAsync(test, expected, expected2);
    }

    [Fact]
    public async Task Skip_Take_WithToListAsync_WithoutOrderBy_ShouldTrigger()
    {
        var test = Usings + @"
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) => Task.FromResult(source.ToList());
    }
}
namespace TestApp
{
    public class AppDbContext : DbContext { public DbSet<User> Users { get; set; } }

    public class Program
    {
        public async Task Main()
        {
            using var db = new AppDbContext();

            // Trigger: Skip/Take without OrderBy, materialized with ToListAsync
            var result = await db.Users.Skip(10).Take(5).ToListAsync();
        }
    }
}" + MockNamespace;

        var expected = VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule)
            .WithLocation(28, 41)
            .WithArguments("Skip");

        var expected2 = VerifyCS.Diagnostic(MissingOrderByAnalyzer.Rule)
            .WithLocation(28, 50)
            .WithArguments("Take");

        await VerifyCS.VerifyAnalyzerAsync(test, expected, expected2);
    }
}
