using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC020_StringContainsWithComparison.StringContainsWithComparisonAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC020_StringContainsWithComparison;

public class StringContainsWithComparisonTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
";

    [Fact]
    public async Task StringContains_WithComparison_InWhere_ShouldTriggerLC020()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<string>().AsQueryable();
            var result = query.Where(x => {|LC020:x.Contains(""abc"", StringComparison.OrdinalIgnoreCase)|}).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StringStartsWith_WithComparison_InFirstOrDefault_ShouldTriggerLC020()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<string>().AsQueryable();
            var result = query.FirstOrDefault(x => {|LC020:x.StartsWith(""abc"", StringComparison.CurrentCulture)|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StringContains_WithoutComparison_InWhere_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<string>().AsQueryable();
            var result = query.Where(x => x.Contains(""abc"")).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StringContains_WithComparison_InLocalCode_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var s = ""test"";
            var result = s.Contains(""t"", StringComparison.OrdinalIgnoreCase);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StringContains_WithComparison_InEnumerable_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var list = new List<string>();
            var result = list.Where(x => x.Contains(""abc"", StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
