using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC042_MissingQueryTags.MissingQueryTagsAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC042_MissingQueryTags;

public class MissingQueryTagsTests
{
    internal const string EfCoreMock = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public DbSet<TEntity> Set<TEntity>() where TEntity : class => null;
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => null;
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
    }

    public interface IIncludableQueryable<out TEntity, out TProperty> : IQueryable<TEntity>
    {
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IIncludableQueryable<TEntity, TProperty> Include<TEntity, TProperty>(this IQueryable<TEntity> source, Expression<Func<TEntity, TProperty>> navigationPropertyPath) where TEntity : class => null;
        public static IIncludableQueryable<TEntity, TProperty> ThenInclude<TEntity, TPreviousProperty, TProperty>(this IIncludableQueryable<TEntity, TPreviousProperty> source, Expression<Func<TPreviousProperty, TProperty>> navigationPropertyPath) where TEntity : class => null;
        public static IQueryable<TEntity> AsNoTracking<TEntity>(this IQueryable<TEntity> source) where TEntity : class => source;
        public static IQueryable<TEntity> IgnoreQueryFilters<TEntity>(this IQueryable<TEntity> source) where TEntity : class => source;
        public static IQueryable<T> TagWith<T>(this IQueryable<T> source, string tag) => source;
        public static IQueryable<T> TagWithCallSite<T>(this IQueryable<T> source, [System.Runtime.CompilerServices.CallerFilePath] string filePath = null, [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0) => source;
        public static Task<List<TSource>> ToListAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => null;
        public static Task<int> CountAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, bool>> predicate, CancellationToken cancellationToken = default) => null;
    }

    public static class RelationalQueryableExtensions
    {
        public static IQueryable<TEntity> AsSplitQuery<TEntity>(this IQueryable<TEntity> source) where TEntity : class => source;
    }
}

namespace TestApp
{
    using Microsoft.EntityFrameworkCore;

    public class Customer { public int Id { get; set; } public string Name { get; set; } public List<Order> Orders { get; set; } }
    public class Order { public int Id { get; set; } public int CustomerId { get; set; } public decimal Total { get; set; } public Customer Customer { get; set; } }

    public class AppDbContext : DbContext
    {
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Order> Orders { get; set; }
    }

    public static class RepositoryExtensions
    {
        public static IQueryable<Order> Recent(this IQueryable<Order> orders) => orders;
    }
}
";

    private static string Wrap(string body, string returnType = "object", bool isAsync = false) => EfCoreMock + @"
namespace TestApp
{
    using Microsoft.EntityFrameworkCore;

    public class Queries
    {
        public " + (isAsync ? "async System.Threading.Tasks.Task<" + returnType + ">" : returnType) + @" Run(AppDbContext db, List<Order> cached)
        {
" + body + @"
        }
    }
}";

    [Fact]
    public async Task FilterSortAndPage_WithoutTag_Reports()
    {
        var test = Wrap(@"            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).{|LC042:ToList|}();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Message_NamesTheTerminalAndScore()
    {
        var test = Wrap(@"            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Skip(20).Take(10).{|#0:ToList|}();");

        await VerifyCS.VerifyAnalyzerAsync(test,
            VerifyCS.Diagnostic("LC042").WithLocation(0).WithArguments("ToList", 4));
    }

    [Fact]
    public async Task IncludeCountsAsAShapeOperator()
    {
        var test = Wrap(@"            return db.Customers.Include(c => c.Orders).Where(c => c.Name != null).OrderBy(c => c.Name).{|LC042:ToList|}();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task JoinCountsTwice()
    {
        // A join alone scores 2, under the default threshold of 3; one more operator reaches it.
        var test = Wrap(@"            var names = db.Orders.Join(db.Customers, o => o.CustomerId, c => c.Id, (o, c) => c.Name).ToList();
            return db.Orders.Join(db.Customers, o => o.CustomerId, c => c.Id, (o, c) => c.Name).Where(n => n != null).{|#0:ToList|}();");

        await VerifyCS.VerifyAnalyzerAsync(test,
            VerifyCS.Diagnostic("LC042").WithLocation(0).WithArguments("ToList", 3));
    }

    [Fact]
    public async Task GroupByCountsTwice()
    {
        var test = Wrap(@"            return db.Orders.GroupBy(o => o.CustomerId).Select(g => g.Key).{|LC042:ToList|}();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SelectManyCountsTwice()
    {
        var test = Wrap(@"            return db.Customers.SelectMany(c => c.Orders).{|LC042:Sum|}(o => o.Total);");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task QuerySyntax_Reports()
    {
        var test = Wrap(@"            return (from o in db.Orders
                    join c in db.Customers on o.CustomerId equals c.Id
                    where c.Name != null
                    select o.Id).{|LC042:ToList|}();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TerminalPredicateCounts()
    {
        var test = Wrap(@"            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).{|LC042:Count|}(o => o.CustomerId == 1);");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncTerminal_Reports()
    {
        var test = Wrap(@"            return await db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Skip(10).{|LC042:ToListAsync|}();",
            "List<Order>", isAsync: true);

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncTerminalPredicate_Reports()
    {
        var test = Wrap(@"            return await db.Orders.Where(o => o.Total > 0).Distinct().{|LC042:CountAsync|}(o => o.CustomerId == 1);",
            "int", isAsync: true);

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SetOfT_IsAQueryRoot()
    {
        var test = Wrap(@"            return db.Set<Order>().Where(o => o.Total > 0).OrderBy(o => o.Id).Take(5).{|LC042:ToArray|}();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task QueryOptions_DoNotCount()
    {
        var test = Wrap(@"            return db.Orders.AsNoTracking().IgnoreQueryFilters().AsSplitQuery().Where(o => o.Total > 0).Select(o => o.Id).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TwoOperators_BelowDefaultThreshold()
    {
        var test = Wrap(@"            return db.Orders.Where(o => o.Total > 0).Select(o => o.Id).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TagWith_Anywhere_Suppresses()
    {
        var test = Wrap(@"            var first = db.Orders.TagWith(""orders"").Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).ToList();
            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).TagWith(""orders"").ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TagWithCallSite_Suppresses()
    {
        var test = Wrap(@"            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).TagWithCallSite().FirstOrDefault();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SubqueryInsideExpressionTree_IsPartOfTheOuterQuery()
    {
        var test = Wrap(@"            return db.Customers
                .Where(c => db.Orders.Where(o => o.CustomerId == c.Id).Where(o => o.Total > 0).OrderBy(o => o.Id).Any())
                .ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task InMemoryQueryable_StaysQuiet()
    {
        var test = Wrap(@"            return cached.AsQueryable().Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LinqToObjects_AfterAsEnumerable_StaysQuiet()
    {
        var test = Wrap(@"            return db.Orders.AsEnumerable().Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UnknownHelperInChain_StaysQuiet()
    {
        // The helper may add its own tag, so the rule cannot prove the query is untagged.
        var test = Wrap(@"            return db.Orders.Recent().Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task QueryStoredInLocal_StaysQuiet()
    {
        var test = Wrap(@"            IQueryable<Order> query = db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id);
            return query.Take(10).ToList();");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StaticInvocationForm_Reports()
    {
        var test = Wrap(@"            return Enumerable.ToList(Queryable.Take(Queryable.OrderBy(Queryable.Where(db.Orders, o => o.Total > 0), o => o.Id), 10));");

        var expected = VerifyCS.Diagnostic("LC042").WithSpan(75, 31, 75, 37).WithArguments("ToList", 3);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Theory]
    [InlineData("5", false)]
    [InlineData("2", true)]
    public async Task ThresholdIsConfigurable(string threshold, bool reports)
    {
        var body = reports
            ? @"            return db.Orders.Where(o => o.Total > 0).Select(o => o.Id).{|LC042:ToList|}();"
            : @"            return db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).ToList();";

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC042_MissingQueryTags.MissingQueryTagsAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestCode = Wrap(body)
        };
        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", $"""
root = true

[*.cs]
dotnet_code_quality.LC042.query_operator_threshold = {threshold}
"""));

        await test.RunAsync();
    }
}
