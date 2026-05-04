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

    private const string NonQueryableEFCoreMock = @"
using System;
using System.Collections.Generic;

namespace Microsoft.EntityFrameworkCore
{
    public sealed class AuditQuery
    {
        public AuditQuery IgnoreQueryFilters() => this;
    }

    public static class CustomEnumerableExtensions
    {
        public static IEnumerable<TEntity> IgnoreQueryFilters<TEntity>(this IEnumerable<TEntity> source) => source;
    }
}
";

    private const string NonEfQueryableMock = @"
using System.Linq;

namespace TestApp
{
    public static class QueryableFilterExtensions
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
    public async Task IgnoreQueryFilters_StaticExtensionCall_ShouldTriggerLC021()
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

    [Fact]
    public async Task IgnoreQueryFilters_InstanceMethodInEfNamespace_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + NonQueryableEFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new AuditQuery();
            var result = query.IgnoreQueryFilters();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IgnoreQueryFilters_ExtensionOnEnumerable_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + NonQueryableEFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var values = new[] { 1, 2, 3 };
            var result = values.IgnoreQueryFilters();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IgnoreQueryFilters_NonEfExtensionOnQueryable_ShouldNotTrigger()
    {
        var test = @"using System.Linq;" + NonEfQueryableMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = TestApp.QueryableFilterExtensions.IgnoreQueryFilters(query).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IgnoreQueryFilters_WithLocalPragmaSuppression_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
#pragma warning disable LC021
            var result = query.IgnoreQueryFilters().ToList();
#pragma warning restore LC021
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IgnoreQueryFilters_WithReviewedSuppressMessage_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        [SuppressMessage(""Security"", ""LC021"", Justification = ""Reviewed tenant-admin bypass."")]
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query.IgnoreQueryFilters().ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
