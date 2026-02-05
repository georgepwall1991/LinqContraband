using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC028_DeepThenInclude.DeepThenIncludeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC028_DeepThenInclude;

public class DeepThenIncludeTests
{
    private const string Preamble = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

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
            Expression<Func<TEntity, TProperty>> nav) where TEntity : class
            => new IncludableQueryable<TEntity, TProperty>();

        public static IIncludableQueryable<TEntity, TProperty> ThenInclude<TEntity, TPrev, TProperty>(
            this IIncludableQueryable<TEntity, TPrev> source,
            Expression<Func<TPrev, TProperty>> nav) where TEntity : class
            => new IncludableQueryable<TEntity, TProperty>();
    }

    public interface IIncludableQueryable<out TEntity, out TProperty> : IQueryable<TEntity> { }

    internal class IncludableQueryable<TEntity, TProperty> : IIncludableQueryable<TEntity, TProperty>
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => null;
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
    }
}

namespace TestApp
{
    public class Order { public int Id { get; set; } public Customer Customer { get; set; } }
    public class Customer { public int Id { get; set; } public Address Address { get; set; } }
    public class Address { public int Id { get; set; } public Country Country { get; set; } }
    public class Country { public int Id { get; set; } public Region Region { get; set; } }
    public class Region { public int Id { get; set; } public Continent Continent { get; set; } }
    public class Continent { public int Id { get; set; } }
}
";

    [Fact]
    public async Task ThenInclude_Depth4_ShouldTriggerLC028()
    {
        var test = Preamble + @"
namespace TestApp
{
    public class TestClass
    {
        public void TestMethod(DbSet<Order> orders)
        {
            var result = orders
                .Include(o => o.Customer)
                .ThenInclude(c => c.Address)
                .ThenInclude(a => a.Country)
                .ThenInclude(c => c.Region)
                {|LC028:.ThenInclude(r => r.Continent)|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ThenInclude_Depth3_ShouldNotTrigger()
    {
        var test = Preamble + @"
namespace TestApp
{
    public class TestClass
    {
        public void TestMethod(DbSet<Order> orders)
        {
            var result = orders
                .Include(o => o.Customer)
                .ThenInclude(c => c.Address)
                .ThenInclude(a => a.Country);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ThenInclude_Depth2_ShouldNotTrigger()
    {
        var test = Preamble + @"
namespace TestApp
{
    public class TestClass
    {
        public void TestMethod(DbSet<Order> orders)
        {
            var result = orders
                .Include(o => o.Customer)
                .ThenInclude(c => c.Address);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Include_Only_ShouldNotTrigger()
    {
        var test = Preamble + @"
namespace TestApp
{
    public class TestClass
    {
        public void TestMethod(DbSet<Order> orders)
        {
            var result = orders
                .Include(o => o.Customer);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
