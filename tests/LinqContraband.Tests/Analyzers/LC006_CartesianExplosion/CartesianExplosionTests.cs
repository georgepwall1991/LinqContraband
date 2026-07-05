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
        public Profile Profile { get; set; }
    }

    public class Order
    {
        public int Id { get; set; }
        public List<Item> Items { get; set; }
        public ICollection<Comment> Comments { get; set; }
        public ICollection<Tag> Tags { get; set; }
    }

    public class Role { }
    public class Address
    {
        public List<Order> Orders { get; set; }
    }

    public class Profile
    {
        public List<Tag> Tags { get; set; }
    }

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
    public async Task TestInnocent_AsSplitQueryOnLocal_NoDiagnostic()
    {
        // The corrected code: AsSplitQuery() is applied, then the query is hoisted to a local
        // before the sibling Includes. The receiver-chain walk must follow the single-assignment
        // local back to the AsSplitQuery() so the split is recognised and the rule stays quiet.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var q = db.Users.AsSplitQuery();
        var query = q.Include(u => u.Orders).Include(u => u.Roles).ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_FirstIncludeAndSplitOnLocal_NoDiagnostic()
    {
        // AsSplitQuery() and the first Include live on the local; the second Include is applied
        // after. Still an effective split, so no diagnostic.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var q = db.Users.AsSplitQuery().Include(u => u.Orders);
        var query = q.Include(u => u.Roles).ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_SiblingCollectionsSplitAcrossLocal_TriggersDiagnostic()
    {
        // One sibling collection Include is on the local, the other is applied after; together
        // they are one query with two sibling collections (a real Cartesian product). Walking the
        // local back to its assignment lets the rule see both Includes and report.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var q = db.Users.Include(u => u.Orders);
        var query = {|LC006:q.Include(u => u.Roles)|}.ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
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
    public async Task TestCrime_ReferencePrefixedSiblingCollections_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users
            .Include(u => u.Address.Orders)
            .Include(u => u.Profile.Tags)
            .ToList();
    }
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC006")
            .WithSpan(14, 21, 16, 42)
            .WithArguments("Address.Orders', 'Profile.Tags");

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
    public async Task TestInnocent_TwoSiblingReferenceIncludes_NoDiagnostic()
    {
        // Boundary lock-in: a Cartesian explosion requires sibling
        // *collection* navigations. Two sibling reference navigations
        // (single-row foreign-key targets) inflate the row count by 1*1,
        // not by collection cardinality, so LC006 must stay quiet on
        // them even though the chain has two siblings.
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.Include(u => u.Address).Include(u => u.Profile).ToList();
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
