using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC022_ToListInSelectProjection.ToListInSelectProjectionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC022_ToListInSelectProjection;

/// <summary>
/// EF Core 8+ with a relational provider translates nested ToList/ToArray/ToHashSet in a projection to the same SQL
/// as the bare collection (checked on 8.0.20 and 10.0.0 with SQLite), and throws on a nested ToDictionary.
/// </summary>
public class ToListInSelectProjectionModernEfTests
{
    // The EF Core version is read from the assembly that declares DbContext; here that is the test
    // compilation itself, so its AssemblyVersion stands in for the referenced EF Core package.
    private static string Source(string version, string body, string provider = "RelationalDatabaseFacadeExtensions") => @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

[assembly: System.Reflection.AssemblyVersion(""" + version + @""")]

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext { }
    public static class " + provider + @" { }

    public static class EntityFrameworkQueryableExtensions
    {
        public static System.Threading.Tasks.Task<List<T>> ToListAsync<T>(this IQueryable<T> source) => null;
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => null;
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
    }
}

public class User { public int Id { get; set; } public List<Order> Orders { get; set; } }
public class Order { public int Id { get; set; } public int Total { get; set; } }

class TestClass
{
    void TestMethod(DbSet<User> users)
    {
        " + body + @"
    }
}
";

    [Theory]
    [InlineData("8.0.0.0", @"var a = users.Select(u => new { u.Id, Ids = u.Orders.Select(o => o.Id).ToList() }).ToList();")]
    [InlineData("9.0.0.0", @"var a = users.Select(u => u.Orders.ToList()).ToList();")]
    [InlineData("10.0.0.0", @"var a = users.Select(u => new { Ids = u.Orders.Select(o => o.Id).ToArray() }).ToList();")]
    [InlineData("10.0.0.0", @"var a = users.Select(u => new { Ids = u.Orders.Select(o => o.Id).ToHashSet() }).ToList();")]
    [InlineData("10.0.0.0", @"var a = users.Select(u => new { N = u.Orders.ToList().Count }).ToList();")]
    public async Task EfCore8Relational_NestedListArrayOrSet_DoesNotReport(string version, string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Source(version, body));
    }

    [Theory]
    [InlineData("8.0.0.0")]
    [InlineData("10.0.0.0")]
    [InlineData("7.0.0.0")]
    public async Task NestedToDictionary_ReportsAsUntranslatable(string version)
    {
        var expected = new DiagnosticResult("LC022", DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments(
                "ToDictionary",
                "cannot be translated by EF Core and throws at run time. Project a list and build the dictionary after the query.");
        await VerifyCS.VerifyAnalyzerAsync(
            Source(version, @"var a = users.Select(u => new { u.Id, ById = {|#0:u.Orders.ToDictionary(o => o.Id, o => o.Total)|} }).ToList();"),
            expected);
    }

    [Fact]
    public async Task EfCore8Relational_NestedAsyncMaterializer_StillReports()
    {
        // ToListAsync returns a task; EF Core does not strip it from a projection the way it strips ToList.
        var expected = new DiagnosticResult("LC022", DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments(
                "ToListAsync",
                "can be expensive or provider-version sensitive. Consider projecting directly or using split queries.");
        await VerifyCS.VerifyAnalyzerAsync(
            Source("10.0.0.0", @"var a = users.Select(u => {|#0:u.Orders.AsQueryable().ToListAsync()|}).ToList();"),
            expected);
    }

    [Fact]
    public async Task EfCore2_NestedToDictionary_KeepsAdvisoryWording()
    {
        // EF Core 2.x evaluates untranslatable fragments on the client instead of throwing.
        var expected = new DiagnosticResult("LC022", DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments(
                "ToDictionary",
                "can be expensive or provider-version sensitive. Consider projecting directly or using split queries.");
        await VerifyCS.VerifyAnalyzerAsync(
            Source("2.2.0.0", @"var a = users.Select(u => new { u.Id, ById = {|#0:u.Orders.ToDictionary(o => o.Id, o => o.Total)|} }).ToList();"),
            expected);
    }

    [Theory]
    // Older EF Core keeps the advisory report.
    [InlineData("7.0.0.0", "RelationalDatabaseFacadeExtensions")]
    [InlineData("6.0.0.0", "RelationalDatabaseFacadeExtensions")]
    // Cosmos, or no relational provider: correlated collections are not translated the same way.
    [InlineData("10.0.0.0", "CosmosDbContextOptionsExtensions")]
    [InlineData("10.0.0.0", "NoProviderMarker")]
    public async Task OlderOrNonRelationalEfCore_NestedToList_StillReports(string version, string provider)
    {
        var expected = new DiagnosticResult("LC022", DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments(
                "ToList",
                "can be expensive or provider-version sensitive. Consider projecting directly or using split queries.");
        await VerifyCS.VerifyAnalyzerAsync(
            Source(version, @"var a = users.Select(u => {|#0:u.Orders.ToList()|}).ToList();", provider),
            expected);
    }

    [Fact]
    public async Task NestedToDictionaryWithoutEfCore_KeepsAdvisoryWording()
    {
        // Another LINQ provider: nothing says EF Core will translate (or reject) the query.
        const string source = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

public class Source<T> : IQueryable<T>
{
    public Type ElementType => typeof(T);
    public Expression Expression => null;
    public IQueryProvider Provider => null;
    public IEnumerator<T> GetEnumerator() => null;
    IEnumerator IEnumerable.GetEnumerator() => null;
}

public class User { public int Id { get; set; } public List<Order> Orders { get; set; } }
public class Order { public int Id { get; set; } public int Total { get; set; } }

class TestClass
{
    void TestMethod(Source<User> users)
    {
        var a = users.Select(u => new { u.Id, ById = {|#0:u.Orders.ToDictionary(o => o.Id, o => o.Total)|} }).ToList();
    }
}
";
        var expected = new DiagnosticResult("LC022", DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments(
                "ToDictionary",
                "can be expensive or provider-version sensitive. Consider projecting directly or using split queries.");
        await VerifyCS.VerifyAnalyzerAsync(source, expected);
    }
}
