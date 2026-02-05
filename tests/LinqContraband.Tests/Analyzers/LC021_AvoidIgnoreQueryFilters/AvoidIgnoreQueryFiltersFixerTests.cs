using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC021_AvoidIgnoreQueryFilters.AvoidIgnoreQueryFiltersAnalyzer,
    LinqContraband.Analyzers.LC021_AvoidIgnoreQueryFilters.AvoidIgnoreQueryFiltersFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC021_AvoidIgnoreQueryFilters;

public class AvoidIgnoreQueryFiltersFixerTests
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
    public async Task IgnoreQueryFilters_InChain_ShouldBeRemoved()
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
        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query.ToList();
        }
    }
}";

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task IgnoreQueryFilters_AfterWhere_ShouldBeRemoved()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = {|LC021:query.Where(x => x > 0).IgnoreQueryFilters()|}.ToList();
        }
    }
}";
        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
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

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task IgnoreQueryFilters_Standalone_ShouldBeRemoved()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = {|LC021:query.IgnoreQueryFilters()|};
        }
    }
}";
        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query;
        }
    }
}";

        await VerifyFix(test, fixedCode);
    }

    private static async Task VerifyFix(string test, string fixedCode)
    {
        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            CompilerDiagnostics = CompilerDiagnostics.None
        };

        await testObj.RunAsync();
    }
}
