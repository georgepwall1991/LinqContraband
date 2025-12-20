using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC027_OrderByAfterPagination.OrderByAfterPaginationAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC027_OrderByAfterPagination;

public class OrderByAfterPaginationTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
";

    [Fact]
    public async Task OrderBy_AfterSkip_ShouldTriggerLC027()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IQueryable<int> query)
        {
            var result = query.Skip(10).{|LC027:OrderBy|}(x => x).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OrderBy_AfterTake_ShouldTriggerLC027()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IQueryable<int> query)
        {
            var result = query.Take(5).{|LC027:OrderBy|}(x => x).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OrderBy_BeforeSkip_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IQueryable<int> query)
        {
            var result = query.OrderBy(x => x).Skip(10).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
