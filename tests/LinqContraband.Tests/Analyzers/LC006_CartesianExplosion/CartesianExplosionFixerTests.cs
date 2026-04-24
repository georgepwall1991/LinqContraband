using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC006_CartesianExplosion.CartesianExplosionAnalyzer,
    LinqContraband.Analyzers.LC006_CartesianExplosion.CartesianExplosionFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC006_CartesianExplosion;

public class CartesianExplosionFixerTests
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
    }

    public class Order
    {
        public int Id { get; set; }
        public List<Item> Items { get; set; }
    }

    public class Role { }
    public class Item { }

    public class DbContext
    {
        public IQueryable<User> Users => new List<User>().AsQueryable();
    }
}";

    [Fact]
    public async Task FixCrime_InjectsAsSplitQuery()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = {|LC006:db.Users.Include(u => u.Orders).Include(u => u.Roles)|}.ToList();
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.AsSplitQuery().Include(u => u.Orders).Include(u => u.Roles).ToList();
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_WithWhereClause_InjectsAsSplitQueryBeforeFirstInclude()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = {|LC006:db.Users.Where(u => u.Id > 0).Include(u => u.Orders).Include(u => u.Roles)|}.ToList();
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.Where(u => u.Id > 0).AsSplitQuery().Include(u => u.Orders).Include(u => u.Roles).ToList();
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_WithFinalAsSingleQuery_ReplacesWithAsSplitQuery()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = {|LC006:db.Users.AsSplitQuery().Include(u => u.Orders).Include(u => u.Roles).AsSingleQuery()|}.ToList();
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.AsSplitQuery().Include(u => u.Orders).Include(u => u.Roles).AsSplitQuery().ToList();
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_StringIncludeChain_InjectsAsSplitQuery()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = {|LC006:db.Users.Include(""Orders"").Include(""Roles"")|}.ToList();
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.AsSplitQuery().Include(""Orders"").Include(""Roles"").ToList();
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_FilteredIncludeChain_InjectsAsSplitQuery()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = {|LC006:db.Users.Include(u => u.Orders.Where(o => o.Id > 0).OrderBy(o => o.Id)).Include(u => u.Roles)|}.ToList();
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.AsSplitQuery().Include(u => u.Orders.Where(o => o.Id > 0).OrderBy(o => o.Id)).Include(u => u.Roles).ToList();
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        await testObj.RunAsync();
    }
}
