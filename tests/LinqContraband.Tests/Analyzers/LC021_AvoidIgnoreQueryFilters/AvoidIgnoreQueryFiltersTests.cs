using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC021_AvoidIgnoreQueryFilters.AvoidIgnoreQueryFiltersAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC021_AvoidIgnoreQueryFilters;

public class AvoidIgnoreQueryFiltersTests
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
    public async Task IgnoreQueryFilters_NamedFilterOverload_ShouldTriggerLC021()
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

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IgnoreQueryFilters_NamedFilterStaticCallWithReorderedArguments_ShouldTriggerLC021()
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

    [Fact]
    public async Task IgnoreQueryFilters_WithReviewedTypeSuppressMessage_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    [SuppressMessage(""Security"", ""LC021"", Justification = ""Reviewed tenant-admin bypasses in this service."")]
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query.IgnoreQueryFilters().ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IgnoreQueryFilters_StaticExtensionCallWithLocalPragmaSuppression_ShouldNotTrigger()
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
            var result = EntityFrameworkQueryableExtensions.IgnoreQueryFilters(query).ToList();
#pragma warning restore LC021
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IgnoreQueryFilters_WithEditorConfigSeverityNone_ShouldNotTrigger()
    {
        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC021_AvoidIgnoreQueryFilters.AvoidIgnoreQueryFiltersAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestCode = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query.IgnoreQueryFilters().ToList();
        }
    }
}"
        };

        test.TestState.AnalyzerConfigFiles.Add(("/0/.editorconfig", """
root = true

[*.cs]
dotnet_diagnostic.LC021.severity = none
"""));

        await test.RunAsync();
    }

    [Fact]
    public async Task IgnoreQueryFilters_InGeneratedFile_ShouldNotTrigger()
    {
        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC021_AvoidIgnoreQueryFilters.AvoidIgnoreQueryFiltersAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestCode = @"// <auto-generated/>
using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query.IgnoreQueryFilters().ToList();
        }
    }
}"
        };

        await test.RunAsync();
    }

    private const string SoftDeleteTypes = @"
namespace TestApp
{
    public class Post
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public bool Deleted { get; set; }
        public bool IsDeleted { get; set; }
        public bool? IsArchived { get; set; }
        public System.DateTime? DeletedAt { get; set; }
        public string DeletedBy { get; set; }
        public bool IsNotDeleted { get; set; }
        public bool Undeleted { get; set; }
    }

    public static class Settings
    {
        public static bool IncludeDeleted { get; set; }
    }
}
";

    private static string SoftDeleteCode(string body) => @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + SoftDeleteTypes + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IQueryable<TestApp.Post> posts, int id)
        {
" + body + @"
        }
    }
}";

    // EF Core 10 named filters: IgnoreQueryFilters([""SoftDelete""]) still turns the named filters off, and a
    // filter name says nothing about what the filter guards, so the named overload keeps reporting.
    [Fact]
    public Task NamedFilterOverload_InlineArray_StillReports() => VerifyCS.VerifyAnalyzerAsync(SoftDeleteCode(@"
            var result = {|LC021:posts.IgnoreQueryFilters(new[] { ""SoftDelete"" })|}.ToList();"));

    [Fact]
    public Task NamedFilterOverload_CollectionExpression_StillReports() => VerifyCS.VerifyAnalyzerAsync(SoftDeleteCode(@"
            var result = {|LC021:posts.IgnoreQueryFilters([""SoftDelete""])|}.ToList();"));

    // Maintenance code that reads soft-deleted or archived rows on purpose: without IgnoreQueryFilters() the
    // query would always be empty.
    [Theory]
    [InlineData("p => p.Deleted")]
    [InlineData("p => p.IsDeleted")]
    [InlineData("p => p.IsDeleted == true")]
    [InlineData("p => p.IsArchived == true")]
    [InlineData("p => p.DeletedAt != null")]
    [InlineData("p => p.DeletedAt.HasValue")]
    [InlineData("p => null != p.DeletedBy")]
    [InlineData("p => p.Id == id && p.IsDeleted")]
    public Task WhereReadsDeletedRows_IsQuiet(string predicate) => VerifyCS.VerifyAnalyzerAsync(SoftDeleteCode(@"
            var result = posts.IgnoreQueryFilters().Where(" + predicate + @").ToList();"));

    [Fact]
    public Task WhereReadsDeletedRows_BeforeIgnoreQueryFilters_IsQuiet() => VerifyCS.VerifyAnalyzerAsync(SoftDeleteCode(@"
            var result = posts.Where(p => p.IsDeleted).IgnoreQueryFilters().ToList();"));

    [Fact]
    public Task WhereReadsDeletedRows_NamedOverload_IsQuiet() => VerifyCS.VerifyAnalyzerAsync(SoftDeleteCode(@"
            var result = posts.IgnoreQueryFilters(new[] { ""SoftDelete"" }).Where(p => p.IsDeleted).ToList();"));

    [Theory]
    [InlineData("p => !p.IsDeleted")]
    [InlineData("p => p.IsDeleted == false")]
    [InlineData("p => p.DeletedAt == null")]
    [InlineData("p => p.IsDeleted || p.TenantId == id")]
    [InlineData("p => p.TenantId == id")]
    [InlineData("p => p.IsNotDeleted")]
    [InlineData("p => p.Undeleted == true")]
    [InlineData("p => TestApp.Settings.IncludeDeleted")]
    [InlineData("p => TestApp.Settings.IncludeDeleted && p.TenantId == id")]
    public Task WhereDoesNotSelectDeletedRows_StillReports(string predicate) => VerifyCS.VerifyAnalyzerAsync(SoftDeleteCode(@"
            var result = {|LC021:posts.IgnoreQueryFilters()|}.Where(" + predicate + @").ToList();"));

    [Fact]
    public Task DeletedFilterInSeparateStatement_StillReports() => VerifyCS.VerifyAnalyzerAsync(SoftDeleteCode(@"
            var all = {|LC021:posts.IgnoreQueryFilters()|};
            var result = all.Where(p => p.IsDeleted).ToList();"));
}
