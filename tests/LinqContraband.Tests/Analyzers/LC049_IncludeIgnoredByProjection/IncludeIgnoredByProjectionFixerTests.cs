using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC049_IncludeIgnoredByProjection.IncludeIgnoredByProjectionAnalyzer,
    LinqContraband.Analyzers.LC049_IncludeIgnoredByProjection.IncludeIgnoredByProjectionFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC049_IncludeIgnoredByProjection;

public class IncludeIgnoredByProjectionFixerTests
{
    private static string Wrap(string body) => IncludeIgnoredByProjectionTests.EFCoreMock + @"
namespace TestApp
{
    class Program
    {
        void Run(AppDbContext db)
        {
" + body + @"
        }
    }
}";

    [Fact]
    public async Task RemovesSingleInclude()
    {
        await new CodeFixTest
        {
            TestCode = Wrap(@"
            var rows = db.Orders
                .{|LC049:Include|}(o => o.Customer)
                .Select(o => new { o.Id, CustomerName = o.Customer.Name })
                .ToList();"),
            FixedCode = Wrap(@"
            var rows = db.Orders
                .Select(o => new { o.Id, CustomerName = o.Customer.Name })
                .ToList();")
        }.RunAsync();
    }

    [Fact]
    public async Task RemovesIncludeWithItsThenIncludes_KeepsOtherOperators()
    {
        await new CodeFixTest
        {
            TestCode = Wrap(@"
            var rows = db.Orders
                .AsNoTracking()
                .{|LC049:Include|}(o => o.Lines).ThenInclude(l => l.Product)
                .Where(o => o.Total > 10)
                .Select(o => new { o.Id, Skus = o.Lines.Select(l => l.Product.Sku).ToList() })
                .ToList();"),
            FixedCode = Wrap(@"
            var rows = db.Orders
                .AsNoTracking()
                .Where(o => o.Total > 10)
                .Select(o => new { o.Id, Skus = o.Lines.Select(l => l.Product.Sku).ToList() })
                .ToList();")
        }.RunAsync();
    }

    [Fact]
    public async Task SingleLineChain()
    {
        await new CodeFixTest
        {
            TestCode = Wrap(@"
            var ids = db.Orders.{|LC049:Include|}(""Lines"").Select(o => o.Id).ToList();"),
            FixedCode = Wrap(@"
            var ids = db.Orders.Select(o => o.Id).ToList();")
        }.RunAsync();
    }

    [Fact]
    public async Task FixAll_RemovesEveryIgnoredIncludeInTheChain()
    {
        await new CodeFixTest
        {
            TestCode = Wrap(@"
            var rows = db.Orders
                .{|LC049:Include|}(o => o.Customer).ThenInclude(c => c.Address)
                .{|LC049:Include|}(o => o.Lines)
                .Select(o => new { o.Id, City = o.Customer.Address.City, Count = o.Lines.Count() })
                .ToList();"),
            FixedCode = Wrap(@"
            var rows = db.Orders
                .Select(o => new { o.Id, City = o.Customer.Address.City, Count = o.Lines.Count() })
                .ToList();"),
            BatchFixedCode = Wrap(@"
            var rows = db.Orders
                .Select(o => new { o.Id, City = o.Customer.Address.City, Count = o.Lines.Count() })
                .ToList();"),
            NumberOfIncrementalIterations = 2,
            NumberOfFixAllIterations = 1
        }.RunAsync();
    }

    [Fact]
    public async Task StaticIncludeCall_OffersNoFix()
    {
        var code = Wrap(@"
            var ids = EntityFrameworkQueryableExtensions.{|LC049:Include|}(db.Orders, o => o.Customer).Select(o => o.Id).ToList();");

        await new CodeFixTest
        {
            TestCode = code,
            FixedCode = code
        }.RunAsync();
    }
}
