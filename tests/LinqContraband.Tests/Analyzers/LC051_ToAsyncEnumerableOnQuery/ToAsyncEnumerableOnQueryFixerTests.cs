using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC051_ToAsyncEnumerableOnQuery.ToAsyncEnumerableOnQueryAnalyzer,
    LinqContraband.Analyzers.LC051_ToAsyncEnumerableOnQuery.ToAsyncEnumerableOnQueryFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC051_ToAsyncEnumerableOnQuery;

public class ToAsyncEnumerableOnQueryFixerTests
{
    private static Task VerifyFixAsync(string before, string after)
    {
        return new CodeFixTest
        {
            TestCode = ToAsyncEnumerableOnQueryTests.Wrap(before),
            FixedCode = ToAsyncEnumerableOnQueryTests.Wrap(after)
        }.RunAsync();
    }

    [Fact]
    public async Task RenamesToAsAsyncEnumerable()
    {
        await VerifyFixAsync(@"
            await foreach (var order in db.Orders.Where(o => o.Total > 0).{|LC051:ToAsyncEnumerable|}())
            {
            }", @"
            await foreach (var order in db.Orders.Where(o => o.Total > 0).AsAsyncEnumerable())
            {
            }");
    }

    [Fact]
    public async Task KeepsMultilineLayout()
    {
        await VerifyFixAsync(@"
            var stream = db.Orders
                .Where(o => o.Total > 0)
                .{|LC051:ToAsyncEnumerable|}() // stream it
                ;", @"
            var stream = db.Orders
                .Where(o => o.Total > 0)
                .AsAsyncEnumerable() // stream it
                ;");
    }

    [Fact]
    public async Task AddsEfCoreUsingWhenMissing()
    {
        var before = ToAsyncEnumerableOnQueryTests.Wrap(@"
            var stream = db.Orders.Where(o => o.Total > 0).{|LC051:ToAsyncEnumerable|}();", usings: string.Empty);
        var after = ToAsyncEnumerableOnQueryTests.Wrap(@"
            var stream = db.Orders.Where(o => o.Total > 0).AsAsyncEnumerable();", usings: string.Empty);
        var afterWithUsing = after.Replace(
            "using System.Linq.Expressions;\n",
            "using System.Linq.Expressions;\nusing Microsoft.EntityFrameworkCore;\n");

        await new CodeFixTest { TestCode = before, FixedCode = afterWithUsing }.RunAsync();
    }

    [Fact]
    public async Task StaticForm_HasNoFix()
    {
        var code = ToAsyncEnumerableOnQueryTests.Wrap(@"
            var stream = AsyncEnumerable.{|LC051:ToAsyncEnumerable|}(db.Orders.Where(o => o.Total > 0));");

        await new CodeFixTest { TestCode = code, FixedCode = code }.RunAsync();
    }

    [Fact]
    public async Task FixAll_RenamesEveryCall()
    {
        await new CodeFixTest
        {
            TestCode = ToAsyncEnumerableOnQueryTests.Wrap(@"
            var a = db.Orders.{|LC051:ToAsyncEnumerable|}();
            var b = db.Set<Order>().Where(o => o.Total > 0).{|LC051:ToAsyncEnumerable|}();"),
            FixedCode = ToAsyncEnumerableOnQueryTests.Wrap(@"
            var a = db.Orders.AsAsyncEnumerable();
            var b = db.Set<Order>().Where(o => o.Total > 0).AsAsyncEnumerable();"),
            BatchFixedCode = ToAsyncEnumerableOnQueryTests.Wrap(@"
            var a = db.Orders.AsAsyncEnumerable();
            var b = db.Set<Order>().Where(o => o.Total > 0).AsAsyncEnumerable();")
        }.RunAsync();
    }
}
