using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC029_RedundantIdentitySelect.RedundantIdentitySelectAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC029_RedundantIdentitySelect;

public class RedundantIdentitySelectTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
";

    [Fact]
    public async Task Select_Identity_ShouldTriggerLC029()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IQueryable<int> query)
        {
            var result = {|LC029:query.Select(x => x)|}.ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Select_NonIdentity_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IQueryable<int> query)
        {
            var result = query.Select(x => x.ToString()).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
