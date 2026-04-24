using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC006_CartesianExplosion.CartesianExplosionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC006_CartesianExplosion;

public class CartesianExplosionTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore; // Mocking this namespace
using Microsoft.EntityFrameworkCore.Query; // For IIncludableQueryable
using TestNamespace;
";

    private const string MockNamespace = @"
namespace Microsoft.EntityFrameworkCore.Query
{
    public interface IIncludableQueryable<out TEntity, out TProperty> : IQueryable<TEntity> { }
}

namespace Microsoft.EntityFrameworkCore
{
    using Microsoft.EntityFrameworkCore.Query;

    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<TEntity> Include<TEntity>(
            this IQueryable<TEntity> source,
            string navigationPropertyPath)
            => source;

        public static IIncludableQueryable<TEntity, TProperty> Include<TEntity, TProperty>(
            this IQueryable<TEntity> source, 
            System.Linq.Expressions.Expression<Func<TEntity, TProperty>> navigationPropertyPath) 
            => null;

        public static IIncludableQueryable<TEntity, TProperty> ThenInclude<TEntity, TPreviousProperty, TProperty>(
            this IIncludableQueryable<TEntity, IEnumerable<TPreviousProperty>> source, 
            System.Linq.Expressions.Expression<Func<TPreviousProperty, TProperty>> navigationPropertyPath)
            => null;

        public static IIncludableQueryable<TEntity, TProperty> ThenInclude<TEntity, TPreviousProperty, TProperty>(
            this IIncludableQueryable<TEntity, TPreviousProperty> source, 
            System.Linq.Expressions.Expression<Func<TPreviousProperty, TProperty>> navigationPropertyPath)
            => null;

        public static IQueryable<T> AsSplitQuery<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> AsSingleQuery<T>(this IQueryable<T> source) => source;
    }
}

namespace TestNamespace
{
    public class User
    {
        public int Id { get; set; }
        public List<Order> Orders { get; set; }
        public List<Role> Roles { get; set; }
        public HashSet<Tag> Tags { get; set; }
        public Address Address { get; set; }
    }

    public class Order
    {
        public int Id { get; set; }
        public List<Item> Items { get; set; }
        public ICollection<Comment> Comments { get; set; }
        public ICollection<Tag> Tags { get; set; }
    }

    public class Role { }
    public class Address { }
    public class Item { }
    public class Comment { }
    public class Tag { }

    public class DbContext
    {
        public IQueryable<User> Users => new List<User>().AsQueryable();
    }
}";

    [Fact]
    public async Task TestCrime_TwoCollectionIncludes_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        // Crime: Including two sibling collections
        var query = db.Users.Include(u => u.Orders).Include(u => u.Roles).ToList();
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC006")
            .WithSpan(15, 21, 15, 74)
            .WithArguments("Orders', 'Roles");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestInnocent_OneCollectionOneReference_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        // Innocent: One collection, one reference
        var query = db.Users.Include(u => u.Address).Include(u => u.Roles).ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_DeepChain_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        // Innocent: Linear chain (Orders -> Items)
        var query = db.Users.Include(u => u.Orders).ThenInclude(o => o.Items).ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_WithAsSplitQuery_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        // Innocent: Using AsSplitQuery()
        var query = db.Users.AsSplitQuery().Include(u => u.Orders).Include(u => u.Roles).ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_WithTrailingAsSplitQuery_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.Include(u => u.Orders).Include(u => u.Roles).AsSplitQuery().ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_WithAsSingleQuery_StillTriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.AsSingleQuery().Include(u => u.Orders).Include(u => u.Roles).ToList();
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC006")
            .WithSpan(14, 21, 14, 90)
            .WithArguments("Orders', 'Roles");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_FinalAsSingleQueryAfterAsSplitQuery_StillTriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.AsSplitQuery().Include(u => u.Orders).Include(u => u.Roles).AsSingleQuery().ToList();
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC006")
            .WithSpan(14, 21, 14, 105)
            .WithArguments("Orders', 'Roles");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_ThreeCollectionIncludes_ReportsOnlyOnce()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.Include(u => u.Orders).Include(u => u.Roles).Include(u => u.Tags).ToList();
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC006")
            .WithSpan(14, 21, 14, 95)
            .WithArguments("Orders', 'Roles', 'Tags");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_NestedSiblingCollectionThenIncludes_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users
            .Include(u => u.Orders)
                .ThenInclude(o => o.Comments)
            .Include(u => u.Orders)
                .ThenInclude(o => o.Tags)
            .ToList();
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC006")
            .WithSpan(14, 21, 18, 42)
            .WithArguments("Comments', 'Tags");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_FilteredSiblingCollectionIncludes_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users
            .Include(u => u.Orders.Where(o => o.Id > 0).OrderBy(o => o.Id))
            .Include(u => u.Roles)
            .ToList();
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC006")
            .WithSpan(14, 21, 16, 35)
            .WithArguments("Orders', 'Roles");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_ResolvableStringIncludeSiblings_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.Include(""Orders"").Include(""Roles"").ToList();
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC006")
            .WithSpan(14, 21, 14, 64)
            .WithArguments("Orders', 'Roles");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestInnocent_DuplicateCollectionInclude_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.Include(u => u.Orders).Include(u => u.Orders).ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_UnresolvableStringInclude_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var navigation = ""Orders"";
        var query = db.Users.Include(navigation).Include(""Roles"").ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
