using LC001 = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer>;
using LC004 = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC004_IQueryableLeak.IQueryableLeakAnalyzer>;
using LC016 = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC016_AvoidDateTimeNow.AvoidDateTimeNowAnalyzer>;
using LC020 = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC020_StringContainsWithComparison.StringContainsWithComparisonAnalyzer>;
using LC022 = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC022_ToListInSelectProjection.ToListInSelectProjectionAnalyzer>;

namespace LinqContraband.Tests.Analyzers;

/// <summary>
/// Unit tests, in-memory repositories and MockQueryable-style fakes build queries with
/// <c>list.AsQueryable()</c>. Those run on LINQ to Objects, so the EF translation rules stay quiet.
/// A source the analysis cannot prove in-memory (a parameter, a DbSet, an <c>IEnumerable</c> that
/// may be a DbSet at runtime, a reassigned local) still reports.
/// </summary>
public class InMemoryQueryableTests
{
    private const string Types = @"
using System;
using System.Collections.Generic;
using System.Linq;

public class Order { public int Id { get; set; } public string Name { get; set; } = """"; public DateTime Placed { get; set; } public List<Order> Children { get; set; } = new List<Order>(); }

public static class QueryableTestExtensions
{
    public static IQueryable<T> BuildMock<T>(this IQueryable<T> source) => source;
}
";

    private static string Code(string body) => Types + @"
public class Tests
{
    readonly List<Order> _orders = new List<Order>();

    static bool IsBig(Order o) => o.Id > 10;
    static Order Enrich(Order o) => o;
    static IQueryable<Order> Wrap(IEnumerable<Order> orders) => orders.AsQueryable();

    void Run(List<Order> list, Order[] array, IEnumerable<Order> sequence, IQueryable<Order> queryable,
        ICollection<Order> collection, IReadOnlyList<Order> readOnlyList, ISet<Order> set, List<IEnumerable<Order>> sequences)
    {
        " + body + @"
    }

    static void Consume(IEnumerable<Order> orders) { foreach (var o in orders) { } }
}
";

    [Theory]
    [InlineData("var q = list.AsQueryable().Where(o => IsBig(o));")]
    [InlineData("var q = array.AsQueryable().Where(o => IsBig(o));")]
    [InlineData("var source = new List<Order>().AsQueryable(); var q = source.Where(o => IsBig(o));")]
    [InlineData("var q = list.AsQueryable().BuildMock().OrderBy(o => o.Id).Where(o => IsBig(o));")]
    [InlineData("var q = new EnumerableQuery<Order>(list).Where(o => IsBig(o));")]
    [InlineData("var q = from o in list.AsQueryable() where IsBig(o) select o;")]
    // SimpleIdServer: an Enumerable operator's result is a LINQ-to-Objects iterator, never a DbSet.
    [InlineData("var q = _orders.Select(r => Enrich(r)).AsQueryable().Where(r => IsBig(r));")]
    [InlineData("var q = list.Where(o => o.Id > 0).OrderBy(o => o.Id).AsQueryable().Where(o => IsBig(o));")]
    [InlineData("var q = array.Concat(list).Cast<Order>().AsQueryable().Where(o => IsBig(o));")]
    [InlineData("var filtered = list.Where(o => o.Id > 0); var q = filtered.AsQueryable().Where(o => IsBig(o));")]
    // No EF query type implements the mutable or read-only collection interfaces.
    [InlineData("var q = collection.AsQueryable().Where(o => IsBig(o));")]
    [InlineData("var q = readOnlyList.AsQueryable().Where(o => IsBig(o));")]
    [InlineData("var q = set.AsQueryable().Where(o => IsBig(o));")]
    // A method in a delegate lambda runs before the query is built, whatever the query's source.
    [InlineData("var q = Wrap(_orders.Select(r => Enrich(r))).Where(o => o.Id > 1);")]
    public Task LC001_InMemoryQueryable_StaysQuiet(string body) => LC001.VerifyAnalyzerAsync(Code(body));

    [Theory]
    [InlineData("var q = queryable.Where(o => {|LC001:IsBig(o)|});")]
    [InlineData("var q = sequence.AsQueryable().Where(o => {|LC001:IsBig(o)|});")]
    [InlineData("var source = list.AsQueryable(); source = queryable; var q = source.Where(o => {|LC001:IsBig(o)|});")]
    // AsEnumerable, Cast and OfType can hand back their source (possibly a DbSet) unchanged.
    [InlineData("var q = queryable.AsEnumerable().AsQueryable().Where(o => {|LC001:IsBig(o)|});")]
    [InlineData("var q = sequence.Cast<Order>().AsQueryable().Where(o => {|LC001:IsBig(o)|});")]
    [InlineData("var q = sequence.OfType<Order>().AsQueryable().Where(o => {|LC001:IsBig(o)|});")]
    // Enumerable operators are lazy: over a query or a bare IEnumerable they still enumerate that source.
    [InlineData("var q = queryable.AsEnumerable().Where(o => o.Id > 0).AsQueryable().Where(o => {|LC001:IsBig(o)|});")]
    [InlineData("var q = sequence.Where(o => o.Id > 0).OrderBy(o => o.Id).AsQueryable().Where(o => {|LC001:IsBig(o)|});")]
    [InlineData("var q = list.Concat(queryable).AsQueryable().Where(o => {|LC001:IsBig(o)|});")]
    [InlineData("var filtered = queryable.AsEnumerable().Select(o => o); var q = filtered.AsQueryable().Where(o => {|LC001:IsBig(o)|});")]
    // An element operator can return a stored sequence, which may be a DbSet.
    [InlineData("var q = sequences.First().AsQueryable().Where(o => {|LC001:IsBig(o)|});")]
    // A delegate lambda nested in the query's expression tree is still translated.
    [InlineData("var q = queryable.Where(o => o.Children.Select(c => {|LC001:Enrich(o)|}).Any());")]
    public Task LC001_UnprovenSource_StillReports(string body) => LC001.VerifyAnalyzerAsync(Code(body));

    [Fact]
    public Task LC016_InMemoryQueryable_StaysQuiet() =>
        LC016.VerifyAnalyzerAsync(Code("var q = list.AsQueryable().Where(o => o.Placed < DateTime.Now);"));

    [Fact]
    public Task LC016_EnumerableOperatorResultAsQueryable_StaysQuiet() =>
        LC016.VerifyAnalyzerAsync(Code("var q = _orders.Select(o => Enrich(o)).AsQueryable().Where(o => o.Placed < DateTime.Now);"));

    [Fact]
    public Task LC016_AsEnumerableOverQueryAsQueryable_StillReports() =>
        LC016.VerifyAnalyzerAsync(Code("var q = queryable.AsEnumerable().AsQueryable().Where(o => o.Placed < {|LC016:DateTime.Now|});"));

    [Fact]
    public Task LC016_EnumerableOperatorOverQueryAsQueryable_StillReports() =>
        LC016.VerifyAnalyzerAsync(Code("var q = queryable.AsEnumerable().Where(o => o.Id > 0).AsQueryable().Where(o => o.Placed < {|LC016:DateTime.Now|});"));

    [Fact]
    public Task LC020_EnumerableOperatorOverQueryAsQueryable_StillReports() =>
        LC020.VerifyAnalyzerAsync(Code(
            "var q = queryable.AsEnumerable().Select(o => o).AsQueryable().Where(o => {|LC020:o.Name.Contains(\"a\", StringComparison.OrdinalIgnoreCase)|});"));

    [Fact]
    public Task LC022_EnumerableOperatorOverQueryAsQueryable_StillReports() =>
        LC022.VerifyAnalyzerAsync(Code("var q = queryable.AsEnumerable().Where(o => o.Id > 0).AsQueryable().Select(o => {|LC022:o.Children.ToList()|});"));

    [Fact]
    public Task LC004_EnumerableOperatorOverQueryAsQueryable_StillReports() =>
        LC004.VerifyAnalyzerAsync(Code("var q = queryable.AsEnumerable().Where(o => o.Id > 1).AsQueryable(); Consume({|LC004:q|});"));

    [Fact]
    public Task LC016_QueryableParameter_StillReports() =>
        LC016.VerifyAnalyzerAsync(Code("var q = queryable.Where(o => o.Placed < {|LC016:DateTime.Now|});"));

    [Fact]
    public Task LC020_InMemoryQueryable_StaysQuiet() =>
        LC020.VerifyAnalyzerAsync(Code(
            "var q = list.AsQueryable().Where(o => o.Name.Contains(\"a\", StringComparison.OrdinalIgnoreCase));"));

    [Fact]
    public Task LC020_CollectionInterfaceAsQueryable_StaysQuiet() =>
        LC020.VerifyAnalyzerAsync(Code(
            "var q = collection.AsQueryable().Where(o => o.Name.Contains(\"a\", StringComparison.OrdinalIgnoreCase));"));

    [Fact]
    public Task LC020_QueryableParameter_StillReports() =>
        LC020.VerifyAnalyzerAsync(Code(
            "var q = queryable.Where(o => {|LC020:o.Name.Contains(\"a\", StringComparison.OrdinalIgnoreCase)|});"));

    [Fact]
    public Task LC022_InMemoryQueryable_StaysQuiet() =>
        LC022.VerifyAnalyzerAsync(Code("var q = list.AsQueryable().Select(o => o.Children.ToList());"));

    [Fact]
    public Task LC022_QueryableParameter_StillReports() =>
        LC022.VerifyAnalyzerAsync(Code("var q = queryable.Select(o => {|LC022:o.Children.ToList()|});"));

    [Fact]
    public Task LC004_InMemoryQueryable_StaysQuiet() =>
        LC004.VerifyAnalyzerAsync(Code("var q = list.AsQueryable().Where(o => o.Id > 1); Consume(q);"));

    [Fact]
    public Task LC004_QueryableParameter_StillReports() =>
        LC004.VerifyAnalyzerAsync(Code("var q = queryable.Where(o => o.Id > 1); Consume({|LC004:q|});"));
}
