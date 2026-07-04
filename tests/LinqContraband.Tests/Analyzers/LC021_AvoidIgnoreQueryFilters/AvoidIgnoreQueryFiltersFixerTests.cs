using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC021_AvoidIgnoreQueryFilters.AvoidIgnoreQueryFiltersAnalyzer,
    LinqContraband.Analyzers.LC021_AvoidIgnoreQueryFilters.AvoidIgnoreQueryFiltersFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using LinqContraband.Analyzers.LC021_AvoidIgnoreQueryFilters;

namespace LinqContraband.Tests.Analyzers.LC021_AvoidIgnoreQueryFilters;

public class AvoidIgnoreQueryFiltersFixerTests
{
    private const string EFCoreMock = @"
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.EntityFrameworkCore
{
    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<TEntity> IgnoreQueryFilters<TEntity>(this IQueryable<TEntity> source) => source;
        public static IQueryable<TEntity> IgnoreQueryFilters<TEntity>(this IQueryable<TEntity> source, IReadOnlyCollection<string> filterKeys) => source;
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
    public async Task IgnoreQueryFilters_BeforeWhere_ShouldRemoveOnlyBypassCall()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = {|LC021:query.IgnoreQueryFilters()|}.Where(x => x > 0).ToList();
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

    [Fact]
    public async Task IgnoreQueryFilters_StaticExtensionCall_ShouldBeRemoved()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = {|LC021:EntityFrameworkQueryableExtensions.IgnoreQueryFilters(query)|}.ToList();
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
    public async Task IgnoreQueryFilters_NamedFilterExtensionCall_ShouldRemoveBypassCall()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var filters = new[] { ""TenantFilter"" };
            var result = {|LC021:query.IgnoreQueryFilters(filters)|}.ToList();
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
            var filters = new[] { ""TenantFilter"" };
            var result = query.ToList();
        }
    }
}";

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task IgnoreQueryFilters_NamedFilterStaticExtensionCall_ShouldBeRemoved()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var filters = new[] { ""TenantFilter"" };
            var result = {|LC021:EntityFrameworkQueryableExtensions.IgnoreQueryFilters(query, filters)|}.ToList();
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
            var filters = new[] { ""TenantFilter"" };
            var result = query.ToList();
        }
    }
}";

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task IgnoreQueryFilters_NamedFilterStaticExtensionCallWithReorderedArguments_ShouldBeRemoved()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var filters = new[] { ""TenantFilter"" };
            var result = {|LC021:EntityFrameworkQueryableExtensions.IgnoreQueryFilters(filterKeys: filters, source: query)|}.ToList();
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
            var filters = new[] { ""TenantFilter"" };
            var result = query.ToList();
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
            FixedCode = fixedCode
        };

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixAll_RewritesAllIgnoreQueryFiltersInstances()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result1 = {|#0:query.IgnoreQueryFilters()|}.Where(x => x > 0).ToList();
            var result2 = {|#1:query.Where(x => x < 100).IgnoreQueryFilters()|}.ToArray();
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
            var result1 = query.Where(x => x > 0).ToList();
            var result2 = query.Where(x => x < 100).ToArray();
        }
    }
}";

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "RemoveIgnoreQueryFilters"
        };

        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC021", DiagnosticSeverity.Warning)
                .WithLocation(0));
        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC021", DiagnosticSeverity.Warning)
                .WithLocation(1));

        await testObj.RunAsync();
    }
}
