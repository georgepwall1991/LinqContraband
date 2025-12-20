using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC028_RedundantMaterialization.RedundantMaterializationAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC028_RedundantMaterialization;

public class RedundantMaterializationTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
";

    [Fact]
    public async Task AsEnumerable_ThenToList_ShouldTriggerLC028()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IQueryable<int> query)
        {
            var result = query.{|LC028:AsEnumerable|}().ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ToList_ThenToArray_ShouldTriggerLC028()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IQueryable<int> query)
        {
            var result = query.ToList().{|LC028:ToArray|}();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SingleMaterializer_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IQueryable<int> query)
        {
            var result = query.ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
