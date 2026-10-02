using System.Collections.Immutable;
using LinqContraband.Analyzers.LC022_ToListInSelectProjection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Tests.Analyzers.LC022_ToListInSelectProjection;

/// <summary>
/// Runs LC022 against the real EF Core 8 assemblies (copied to efcore/ by the test project, Relational included), so
/// the version and provider gate is checked against EF Core's own assembly identity, not a mock.
/// </summary>
public class ToListInSelectProjectionRealEfCoreTests
{
    private const string Source = @"
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

public class Order { public int Id { get; set; } public int Total { get; set; } }
public class Customer { public int Id { get; set; } public List<Order> Orders { get; set; } = new(); }

public sealed class ShopContext : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
}

public class Program
{
    public void Run(ShopContext db)
    {
        var lists = db.Customers.Select(c => new { c.Id, Ids = c.Orders.Select(o => o.Id).ToList() }).ToList();
        var arrays = db.Customers.Select(c => new { c.Id, Ids = c.Orders.Select(o => o.Id).ToArray() }).ToList();
        var sets = db.Customers.Select(c => new { c.Id, Ids = c.Orders.Select(o => o.Id).ToHashSet() }).ToList();
        var counts = db.Customers.Select(c => new { c.Id, N = c.Orders.ToList().Count }).ToList();
        var dictionaries = db.Customers.Select(c => new { c.Id, ById = c.Orders.ToDictionary(o => o.Id, o => o.Total) }).ToList();
    }
}
";

    [Fact]
    public async Task EfCore8Relational_ReportsOnlyTheNestedToDictionary()
    {
        var compilation = CSharpCompilation.Create(
            "RealEfCoreLc022",
            new[] { CSharpSyntaxTree.ParseText(Source) },
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ToListInSelectProjectionAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("LC022", diagnostic.Id);
        Assert.Contains("ToDictionary", diagnostic.GetMessage());
        Assert.Contains("cannot be translated", diagnostic.GetMessage());
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        foreach (var path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
            yield return MetadataReference.CreateFromFile(path);

        // Copied by the test project's CopyEfCoreTestReferences target.
        foreach (var path in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "efcore"), "*.dll"))
            yield return MetadataReference.CreateFromFile(path);
    }
}
