using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC057_EmptyQueryAggregate.EmptyQueryAggregateAnalyzer,
    LinqContraband.Analyzers.LC057_EmptyQueryAggregate.EmptyQueryAggregateFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC057_EmptyQueryAggregate;

/// <summary>
/// Every fixed document is compiled by the verifier, so a test here fails if the fix adds a compiler error.
/// </summary>
public class EmptyQueryAggregateFixerTests
{
    private static string Wrap(string body) => EmptyQueryAggregateTests.Wrap(body);

    private static Task VerifyFixAsync(string before, string after)
    {
        return new CodeFixTest { TestCode = Wrap(before), FixedCode = Wrap(after) }.RunAsync();
    }

    private static Task VerifyNoFixAsync(string code)
    {
        return new CodeFixTest { TestCode = Wrap(code), FixedCode = Wrap(code) }.RunAsync();
    }

    [Theory]
    // The result keeps its type: the nullable result falls back to default.
    [InlineData(
        @"var max = db.Products.{|LC057:Max|}(p => p.Price);",
        @"var max = db.Products.Max(p => (decimal?)p.Price) ?? default;")]
    [InlineData(
        @"var min = db.Products.{|LC057:Min|}(p => p.Created);",
        @"var min = db.Products.Min(p => (DateTime?)p.Created) ?? default;")]
    [InlineData(
        @"var average = db.Products.{|LC057:Average|}(p => p.CategoryId);",
        @"var average = db.Products.Average(p => (int?)p.CategoryId) ?? default;")]
    [InlineData(
        @"var max = db.Products.{|LC057:Max|}(p => p.Price * 2);",
        @"var max = db.Products.Max(p => (decimal?)(p.Price * 2)) ?? default;")]
    [InlineData(
        @"var max = db.Products.{|LC057:Max|}((Product p) => p.Price);",
        @"var max = db.Products.Max((Product p) => (decimal?)p.Price) ?? default;")]
    [InlineData(
        @"var max = Queryable.{|LC057:Max|}(db.Products, p => p.Price);",
        @"var max = Queryable.Max(db.Products, p => (decimal?)p.Price) ?? default;")]
    // Async forms, awaited directly or through ConfigureAwait.
    [InlineData(
        @"var max = await db.Products.{|LC057:MaxAsync|}(p => p.Price, ct);",
        @"var max = await db.Products.MaxAsync(p => (decimal?)p.Price, ct) ?? default;")]
    [InlineData(
        @"var average = await db.Products.{|LC057:AverageAsync|}(p => p.CategoryId, ct).ConfigureAwait(false);",
        @"var average = await db.Products.AverageAsync(p => (int?)p.CategoryId, ct).ConfigureAwait(false) ?? default;")]
    // Selector-less forms get a casting selector.
    [InlineData(
        @"var max = db.Products.Select(p => p.Price).{|LC057:Max|}();",
        @"var max = db.Products.Select(p => p.Price).Max(x => (decimal?)x) ?? default;")]
    [InlineData(
        @"var min = await db.Products.Select(p => p.Id).{|LC057:MinAsync|}(ct);",
        @"var min = await db.Products.Select(p => p.Id).MinAsync(x => (int?)x, ct) ?? default;")]
    [InlineData(
        @"var x = 1; var max = db.Products.Select(p => p.Price).{|LC057:Max|}();",
        @"var x = 1; var max = db.Products.Select(p => p.Price).Max(value => (decimal?)value) ?? default;")]
    // Inside a larger expression the fallback is parenthesized.
    [InlineData(
        @"var next = db.Products.{|LC057:Max|}(p => p.Id) + 1;",
        @"var next = (db.Products.Max(p => (int?)p.Id) ?? default) + 1;")]
    [InlineData(
        @"var text = db.Products.{|LC057:Max|}(p => p.Price).ToString(""N2"");",
        @"var text = (db.Products.Max(p => (decimal?)p.Price) ?? default).ToString(""N2"");")]
    [InlineData(
        @"Console.WriteLine(db.Products.{|LC057:Max|}(p => p.Price));",
        @"Console.WriteLine(db.Products.Max(p => (decimal?)p.Price) ?? default);")]
    // Already stored as nullable: only the cast is needed.
    [InlineData(
        @"decimal? max = db.Products.{|LC057:Max|}(p => p.Price);",
        @"decimal? max = db.Products.Max(p => (decimal?)p.Price);")]
    [InlineData(
        @"double? average = await db.Products.{|LC057:AverageAsync|}(p => p.CategoryId, ct);",
        @"double? average = await db.Products.AverageAsync(p => (int?)p.CategoryId, ct);")]
    public async Task CastsTheValueToNullable(string before, string after)
    {
        await VerifyFixAsync(before, after);
    }

    [Fact]
    public async Task ExpressionBodiedMember_KeepsItsReturnType()
    {
        var before = EmptyQueryAggregateTests.Wrap(string.Empty).Replace(
            "class Program\n{",
            "class Program\n{\n    decimal Top(ShopContext db) => db.Products.{|LC057:Max|}(p => p.Price);\n");
        var after = EmptyQueryAggregateTests.Wrap(string.Empty).Replace(
            "class Program\n{",
            "class Program\n{\n    decimal Top(ShopContext db) => db.Products.Max(p => (decimal?)p.Price) ?? default;\n");
        Assert.NotEqual(before, EmptyQueryAggregateTests.Wrap(string.Empty));

        await new CodeFixTest { TestCode = before, FixedCode = after }.RunAsync();
    }

    [Theory]
    // A selector that is not a lambda has no expression to cast.
    [InlineData(@"Expression<Func<Product, decimal>> selector = p => p.Price; var max = db.Products.{|LC057:Max|}(selector);")]
    // The task is not awaited where it is created, so its type would change.
    [InlineData(@"Task<decimal> pending = db.Products.{|LC057:MaxAsync|}(p => p.Price, ct); var max = await pending;")]
    // The type is fixed by an explicit generic argument.
    [InlineData(@"var max = db.Products.{|LC057:Max<Product, decimal>|}(p => p.Price);")]
    public async Task NoSafeRewrite_OffersNoFix(string code)
    {
        await VerifyNoFixAsync(code);
    }

    [Fact]
    public async Task DiscardedResult_OnlyCasts()
    {
        await VerifyFixAsync(
            @"db.Products.{|LC057:Max|}(p => p.Price);",
            @"db.Products.Max(p => (decimal?)p.Price);");
    }

    [Fact]
    public async Task FixAll_FixesEveryAggregate()
    {
        var before = Wrap(@"
        var max = db.Products.{|LC057:Max|}(p => p.Price);
        var min = await db.Products.{|LC057:MinAsync|}(p => p.Created, ct);
        var average = db.Products.Select(p => p.CategoryId).{|LC057:Average|}();");
        var after = Wrap(@"
        var max = db.Products.Max(p => (decimal?)p.Price) ?? default;
        var min = await db.Products.MinAsync(p => (DateTime?)p.Created, ct) ?? default;
        var average = db.Products.Select(p => p.CategoryId).Average(x => (int?)x) ?? default;");

        await new CodeFixTest { TestCode = before, FixedCode = after, BatchFixedCode = after }.RunAsync();
    }

    /// <summary>
    /// The fixer-coverage corpus: every shape <see cref="EmptyQueryAggregateTests"/> reports either gets a fix
    /// that compiles (the verifier compiles it) or no fix at all.
    /// </summary>
    [Theory]
    [InlineData(
        @"var query = db.Products.Where(p => p.CategoryId == categoryId); var max = query.{|LC057:Max|}(p => p.Price);",
        @"var query = db.Products.Where(p => p.CategoryId == categoryId); var max = query.Max(p => (decimal?)p.Price) ?? default;")]
    [InlineData(
        @"var query = db.Products.AsQueryable(); query = query.Where(p => p.CategoryId == categoryId); var max = query.{|LC057:Max|}(p => p.Price);",
        @"var query = db.Products.AsQueryable(); query = query.Where(p => p.CategoryId == categoryId); var max = query.Max(p => (decimal?)p.Price) ?? default;")]
    [InlineData(
        @"var max = repo.Query().{|LC057:Max|}(p => p.Price);",
        @"var max = repo.Query().Max(p => (decimal?)p.Price) ?? default;")]
    [InlineData(
        @"var max = repo.Active().{|LC057:Max|}(p => p.Price);",
        @"var max = repo.Active().Max(p => (decimal?)p.Price) ?? default;")]
    [InlineData(
        @"var max = db.Products.InCategory(categoryId).{|LC057:Max|}(p => p.Price);",
        @"var max = db.Products.InCategory(categoryId).Max(p => (decimal?)p.Price) ?? default;")]
    [InlineData(
        @"var max = db.Set<Product>().Where(p => p.CategoryId == categoryId).{|LC057:Max|}(p => p.Price);",
        @"var max = db.Set<Product>().Where(p => p.CategoryId == categoryId).Max(p => (decimal?)p.Price) ?? default;")]
    [InlineData(
        @"var max = db.Products.AsNoTracking().{|LC057:Max|}(p => p.Price);",
        @"var max = db.Products.AsNoTracking().Max(p => (decimal?)p.Price) ?? default;")]
    [InlineData(
        @"var average = db.Products.{|LC057:Average|}(p => p.Price);",
        @"var average = db.Products.Average(p => (decimal?)p.Price) ?? default;")]
    [InlineData(
        @"var average = await db.Products.Select(p => p.Price).{|LC057:AverageAsync|}();",
        @"var average = await db.Products.Select(p => p.Price).AverageAsync(x => (decimal?)x) ?? default;")]
    [InlineData(
        @"var max = await db.Products.Select(p => p.Id).{|LC057:MaxAsync|}(ct);",
        @"var max = await db.Products.Select(p => p.Id).MaxAsync(x => (int?)x, ct) ?? default;")]
    [InlineData(
        @"var max = db.Products.{|LC057:Max|}(p => (long)p.Id);",
        @"var max = db.Products.Max(p => (long?)(long)p.Id) ?? default;")]
    [InlineData(
        @"if (db.Categories.Any()) { var max = db.Products.{|LC057:Max|}(p => p.Price); }",
        @"if (db.Categories.Any()) { var max = db.Products.Max(p => (decimal?)p.Price) ?? default; }")]
    public async Task ReportedShapes_FixCompiles(string before, string after)
    {
        await VerifyFixAsync(before, after);
    }
}
