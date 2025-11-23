using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC004_GuidInQuery.GuidInQueryAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC004_GuidInQuery;

public class GuidInQueryTests
{
    private const string Usings = @"
using System;
using System.Collections.Generic;
using System.Linq;
";

    [Fact]
    public async Task NewGuid_InsideIQueryable_ShouldTriggerLC004()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            var result = query.Where(x => x == 1 && {|LC004:Guid.NewGuid()|} != Guid.Empty);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NewGuid_Constructor_InsideIQueryable_ShouldTriggerLC004()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            // Using string constructor for example
            var result = query.Select(x => {|LC004:new Guid(""00000000-0000-0000-0000-000000000000"")|});
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NewGuid_OutsideIQueryable_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var id = Guid.NewGuid();
            var query = new List<int>().AsQueryable();
            var result = query.Where(x => x == 1);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NewGuid_InsideEnumerable_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var list = new List<int>();
            var result = list.Where(x => x == 1 && Guid.NewGuid() != Guid.Empty);
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}