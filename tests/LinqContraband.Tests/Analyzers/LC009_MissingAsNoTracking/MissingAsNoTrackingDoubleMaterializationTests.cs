using VerifyCS = LinqContraband.Tests.Analyzers.LC009_MissingAsNoTracking.ReturnedEntitiesOptInVerifier;

namespace LinqContraband.Tests.Analyzers.LC009_MissingAsNoTracking;

/// <summary>
/// <c>db.Orders.ToList().Where(...).ToList()</c> runs one EF query: the inner <c>ToList()</c>. The outer
/// materializer is LINQ to Objects over a list that is already loaded, so LC009 reports once, on the inner call,
/// and the fix puts <c>AsNoTracking()</c> on the EF source.
/// </summary>
public class MissingAsNoTrackingDoubleMaterializationTests
{
    private const string Mock = @"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TestNamespace;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public int SaveChanges() => 0;
        public DbSet<T> Set<T>() where T : class => null;
    }

    public class DbSet<T> : IQueryable<T> where T : class
    {
        public Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) => source;
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) => Task.FromResult(new List<T>());
        public static Task<T[]> ToArrayAsync<T>(this IQueryable<T> source) => Task.FromResult(new T[0]);
    }
}

namespace TestNamespace
{
    public class Order { public int Id { get; set; } public decimal Total { get; set; } }

    public class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
    }
}
";

    private static string Program(string body, string returnType = "object") => Mock + @"
class Program
{
    public async Task<" + returnType + @"> Run(AppDbContext db)
    {
        " + body + @"
    }
}
";

    [Theory]
    [InlineData("return {|LC009:db.Orders.ToList()|}.Where(o => o.Total > 1).ToList();")]
    [InlineData("return {|LC009:db.Orders.ToList()|}.OrderBy(o => o.Id).Take(5).ToList();")]
    [InlineData("return {|LC009:db.Orders.Where(o => o.Total > 1).ToList()|}.First();")]
    [InlineData("return {|LC009:db.Orders.ToArray()|}.ToArray();")]
    [InlineData("return {|LC009:db.Orders.ToList()|}.Where(o => o.Total > 1).ToDictionary(o => o.Id);")]
    [InlineData("return {|LC009:db.Orders.ToList()|}.Where(o => o.Id > 0).ToList().FirstOrDefault();")]
    [InlineData("return (await {|LC009:db.Orders.ToListAsync()|}).Where(o => o.Total > 1).ToList();")]
    [InlineData("return (await {|LC009:db.Orders.ToArrayAsync()|}).OrderBy(o => o.Id).ToList();")]
    [InlineData("var orders = await {|LC009:db.Orders.ToListAsync()|}; return orders.Where(o => o.Total > 1).ToList();")]
    public Task InMemoryMaterializerOverLoadedList_ReportsOnceOnTheEfQuery(string body) =>
        VerifyCS.VerifyAnalyzerAsync(Program(body));

    [Theory]
    // The inner query opted out of tracking: nothing to report on either materializer.
    [InlineData("return db.Orders.AsNoTracking().ToList().Where(o => o.Total > 1).ToList();")]
    [InlineData("return (await db.Orders.AsNoTracking().ToListAsync()).Where(o => o.Total > 1).ToList();")]
    // The loaded entities are changed and saved: a write path on the inner query, and nothing for the outer.
    [InlineData("foreach (var o in db.Orders.ToList().Where(o => o.Total > 1).ToList()) { o.Total = 0; } db.SaveChanges(); return null;")]
    // The entities are changed through the outer, in-memory result: a write path even with no SaveChanges in sight.
    [InlineData("var big = db.Orders.ToList().Where(o => o.Total > 1).ToList(); foreach (var o in big) { o.Total = 0; } return null;")]
    [InlineData("foreach (var o in (await db.Orders.ToListAsync()).Where(o => o.Total > 1).ToList()) { o.Total = 0; } return null;")]
    [InlineData("db.Orders.ToList().Where(o => o.Total > 1).First().Total = 0; return null;")]
    public Task NoTrackingOrWritePath_StaysQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(Program(body));

    [Fact]
    public Task Fix_AddsAsNoTrackingToTheEfSource_Sync() =>
        VerifyCS.VerifyCodeFixAsync(
            Program("var totals = {|LC009:db.Orders.ToList()|}.Where(o => o.Total > 1).ToList(); return totals.Count;"),
            Program("var totals = db.Orders.AsNoTracking().ToList().Where(o => o.Total > 1).ToList(); return totals.Count;"));

    [Fact]
    public Task EntitiesReturnedThroughTheOuterMaterializer_ReportWithoutAFix()
    {
        var source = Program("return {|LC009:db.Orders.ToList()|}.Where(o => o.Total > 1).ToList();");
        return VerifyCS.VerifyCodeFixAsync(source, source);
    }

    [Fact]
    public Task Fix_AddsAsNoTrackingToTheEfSource_Async() =>
        VerifyCS.VerifyCodeFixAsync(
            Program("var totals = (await {|LC009:db.Orders.ToListAsync()|}).Where(o => o.Total > 1).ToList(); return totals.Count;"),
            Program("var totals = (await db.Orders.AsNoTracking().ToListAsync()).Where(o => o.Total > 1).ToList(); return totals.Count;"));
    [Theory]
    // AsEnumerable() defers: the query runs when the outer materializer enumerates it, so that call reports.
    [InlineData("return {|LC009:db.Orders.AsEnumerable().Where(o => o.Total > 1).ToList()|};")]
    [InlineData("return {|LC009:db.Orders.AsEnumerable().ToList()|};")]
    [InlineData("return {|LC009:db.Orders.Where(o => o.Id > 0).AsEnumerable().OrderBy(o => o.Id).ToArray()|};")]
    [InlineData("return {|LC009:db.Orders.AsEnumerable().Where(o => o.Total > 1).First()|};")]
    [InlineData("return {|LC009:db.Set<Order>().AsEnumerable().Where(o => o.Total > 1).ToList()|};")]
    // No outer materializer that reports (none at all, one LC009 does not cover, or a projection it skips):
    // AsEnumerable() stays the reported call.
    [InlineData("foreach (var o in {|LC009:db.Orders.AsEnumerable()|}.Where(o => o.Total > 1)) { Console.WriteLine(o.Id); } return null;")]
    [InlineData("return {|LC009:db.Orders.AsEnumerable()|}.Where(o => o.Total > 1);")]
    [InlineData("return {|LC009:db.Orders.AsEnumerable()|}.ToLookup(o => o.Id);")]
    [InlineData("return {|LC009:db.Orders.AsEnumerable()|}.Select(o => o.Total).ToList();")]
    [InlineData("return {|LC009:db.Orders.AsEnumerable()|}.Select(o => o).ToList();")]
    public Task AsEnumerableThenMaterializer_ReportsOnce(string body) =>
        VerifyCS.VerifyAnalyzerAsync(Program(body));

    [Theory]
    [InlineData("return db.Orders.AsNoTracking().AsEnumerable().Where(o => o.Total > 1).ToList();")]
    [InlineData("var big = db.Orders.AsEnumerable().Where(o => o.Total > 1).ToList(); foreach (var o in big) { o.Total = 0; } return null;")]
    [InlineData("foreach (var o in db.Orders.AsEnumerable().Where(o => o.Total > 1).ToList()) { o.Total = 0; } db.SaveChanges(); return null;")]
    public Task AsEnumerableThenMaterializer_NoTrackingOrWritePath_StaysQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(Program(body));

    [Fact]
    public Task Fix_AsEnumerableThenMaterializer_AddsAsNoTrackingToTheEfSource() =>
        VerifyCS.VerifyCodeFixAsync(
            Program("var totals = {|LC009:db.Orders.AsEnumerable().Where(o => o.Total > 1).ToList()|}; return totals.Count;"),
            Program("var totals = db.Orders.AsNoTracking().AsEnumerable().Where(o => o.Total > 1).ToList(); return totals.Count;"));
}
