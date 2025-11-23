using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
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
    }
}

namespace TestNamespace
{
    public class User
    {
        public int Id { get; set; }
        public List<Order> Orders { get; set; }
        public List<Role> Roles { get; set; }
        public Address Address { get; set; }
    }

    public class Order
    {
        public int Id { get; set; }
        public List<Item> Items { get; set; }
    }

    public class Role { }
    public class Address { }
    public class Item { }

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

        // The diagnostic spans the entire chain up to the second Include.
        // Line 15 starts with `var query = ...`
        // The usage `db.Users...` starts at column 21.
        var expected = VerifyCS.Diagnostic("LC006")
            .WithSpan(15, 21, 15, 74) 
            .WithArguments("List<Role>");

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
}
