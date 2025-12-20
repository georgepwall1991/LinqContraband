using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC021_AvoidIgnoreQueryFilters.AvoidIgnoreQueryFiltersAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC021_AvoidIgnoreQueryFilters;

public class AvoidIgnoreQueryFiltersTests
{
    private const string EFCoreMock = @"
using System;
using System.Linq;

namespace Microsoft.EntityFrameworkCore
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<TEntity> IgnoreQueryFilters<TEntity>(this IQueryable<TEntity> source) => source;
    }
}
";

    [Fact]
    public async Task IgnoreQueryFilters_OnIQueryable_ShouldTriggerLC021()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = {|LC021:query.IgnoreQueryFilters()|}.ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NoIgnoreQueryFilters_OnIQueryable_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query.Where(x => x > 0).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
