using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC019_ConditionalInclude.ConditionalIncludeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC019_ConditionalInclude;

public class ConditionalIncludeTests
{
    private const string EFCoreMock = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Microsoft.EntityFrameworkCore.Query
{
    public interface IIncludableQueryable<out TEntity, out TProperty> : IQueryable<TEntity> { }
}

namespace Microsoft.EntityFrameworkCore
{
    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => null;
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IIncludableQueryable<TEntity, TProperty> Include<TEntity, TProperty>(
            this IQueryable<TEntity> source,
            Expression<Func<TEntity, TProperty>> navigationPropertyPath) where TEntity : class => null;

        public static IIncludableQueryable<TEntity, TProperty> ThenInclude<TEntity, TPreviousProperty, TProperty>(
            this IIncludableQueryable<TEntity, TPreviousProperty> source,
            Expression<Func<TPreviousProperty, TProperty>> navigationPropertyPath) where TEntity : class => null;
    }
}
";

    [Fact]
    public async Task Include_WithTernary_ShouldTriggerLC019()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Order { public int Id { get; set; } public Customer Customer { get; set; } public Supplier Supplier { get; set; } }
    public class Customer { public int Id { get; set; } }
    public class Supplier { public int Id { get; set; } }

    public class TestClass
    {
        public void TestMethod(DbSet<Order> orders, bool useCustomer)
        {
            var result = {|LC019:orders.Include(o => useCustomer ? (object)o.Customer : o.Supplier)|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Include_WithCoalesce_ShouldTriggerLC019()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Order { public int Id { get; set; } public Customer Customer { get; set; } public Customer FallbackCustomer { get; set; } }
    public class Customer { public int Id { get; set; } }

    public class TestClass
    {
        public void TestMethod(DbSet<Order> orders)
        {
            var result = {|LC019:orders.Include(o => o.Customer ?? o.FallbackCustomer)|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Include_Normal_ShouldNotTrigger()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Order { public int Id { get; set; } public Customer Customer { get; set; } }
    public class Customer { public int Id { get; set; } }

    public class TestClass
    {
        public void TestMethod(DbSet<Order> orders)
        {
            var result = orders.Include(o => o.Customer);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ThenInclude_Normal_ShouldNotTrigger()
    {
        var test = EFCoreMock + @"
namespace TestApp
{
    public class Order { public int Id { get; set; } public Customer Customer { get; set; } }
    public class Customer { public int Id { get; set; } public Address Address { get; set; } }
    public class Address { public int Id { get; set; } }

    public class TestClass
    {
        public void TestMethod(DbSet<Order> orders)
        {
            var result = orders.Include(o => o.Customer).ThenInclude(c => c.Address);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
