using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC024_GroupByNonTranslatable.GroupByNonTranslatableAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC024_GroupByNonTranslatable;

public partial class GroupByNonTranslatableTests
{
    // The EF Core version is read from the assembly that declares DbContext; here that is the test
    // compilation itself, so its AssemblyVersion stands in for the referenced EF Core package.
    private static string EfCoreVersion(string version, string provider = "RelationalDatabaseFacadeExtensions") => Usings + @"
[assembly: System.Reflection.AssemblyVersion(""" + version + @""")]
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext { }
    public static class " + provider + @" { }
}
";

    private const string GroupShapes = @"
namespace TestApp
{
    public class Form { public string CorrelationId { get; set; } public int Version { get; set; } public int Flag { get; set; } }

    public class TestClass
    {
        public void TestMethod(IQueryable<Form> forms, List<string> ids)
        {
            // SimpleIdServer FormBuilder: latest version per correlation id.
            var latest = forms.OrderByDescending(f => f.Version).GroupBy(f => f.CorrelationId).Select(g => g.First());
            var ordered = forms.GroupBy(f => f.CorrelationId).Select(g => g.OrderByDescending(f => f.Version).FirstOrDefault());
            var filtered = forms.GroupBy(f => f.CorrelationId).Select(g => g.FirstOrDefault(f => f.Version > 1));
            var last = forms.GroupBy(f => f.CorrelationId).Select(g => new { g.Key, Last = g.OrderBy(f => f.Version).Last() });
            var single = forms.GroupBy(f => f.CorrelationId).Select(g => g.Single());
            var sub = forms.GroupBy(f => f.CorrelationId).Select(g => g.Where(f => f.Version > 1));
            var items = forms.GroupBy(f => f.CorrelationId).Select(g => new { g.Key, Items = g.ToList() });
            var converted = forms.GroupBy(f => f.CorrelationId).Select(g => new { g.Key, B = Convert.ToBoolean(g.First().Flag) });
            var query = from f in forms group f by f.CorrelationId into g select g.OrderBy(x => x.Version).First();
        }
    }
}";

    [Fact]
    public async Task EfCore8_GroupElementAccessorsAndSubsequences_DoNotTrigger()
    {
        await VerifyCS.VerifyAnalyzerAsync(EfCoreVersion("8.0.0.0") + GroupShapes);
    }

    [Fact]
    public async Task EfCore9_GroupElementAccessorsAndSubsequences_DoNotTrigger()
    {
        await VerifyCS.VerifyAnalyzerAsync(EfCoreVersion("9.0.0.0") + GroupShapes);
    }

    [Theory]
    [InlineData("CosmosDbContextOptionsExtensions")]
    [InlineData("NoProviderMarker")]
    public async Task EfCore9_WithoutRelationalProvider_GroupElementAccessor_StillTriggers(string provider)
    {
        // Cosmos, or a project with no relational provider, does not translate these GroupBy shapes.
        var test = EfCoreVersion("9.0.0.0", provider) + @"
namespace TestApp
{
    public class Form { public string CorrelationId { get; set; } }

    public class TestClass
    {
        public void TestMethod(IQueryable<Form> forms)
        {
            var latest = forms.GroupBy(f => f.CorrelationId).Select(g => {|LC024:g.First()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EfCore6_GroupElementAccessor_StillTriggers()
    {
        var test = EfCoreVersion("6.0.0.0") + @"
namespace TestApp
{
    public class Form { public string CorrelationId { get; set; } public int Version { get; set; } }

    public class TestClass
    {
        public void TestMethod(IQueryable<Form> forms)
        {
            var latest = forms.GroupBy(f => f.CorrelationId).Select(g => {|LC024:g.First()|});
            var items = forms.GroupBy(f => f.CorrelationId).Select(g => new { g.Key, Items = {|LC024:g.ToList()|} });
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EfCore9_UntranslatableGroupUses_StillTrigger()
    {
        var test = EfCoreVersion("9.0.0.0") + @"
namespace TestApp
{
    public class Form { public string CorrelationId { get; set; } public int Version { get; set; } }
    public class Summary { public Summary(IEnumerable<Form> forms) { } }

    public class TestClass
    {
        private static int Scale(int value) => value * 2;
        private static Form Pick(IEnumerable<Form> forms) => null;

        public void TestMethod(IQueryable<Form> forms)
        {
            var helper = forms.GroupBy(f => f.CorrelationId).Select(g => {|LC024:Pick(g)|});
            var scaled = forms.GroupBy(f => f.CorrelationId).Select(g => {|LC024:g.Select(f => Scale(f.Version)).First()|});
            var created = forms.GroupBy(f => f.CorrelationId).Select(g => new Summary({|LC024:g|}));
            var unordered = forms.GroupBy(f => f.CorrelationId).Select(g => {|LC024:g.Last()|});
            var afterList = forms.GroupBy(f => f.CorrelationId).Select(g => {|LC024:g.ToList().Where(f => f.Version > 1)|});
            var distinctLast = forms.GroupBy(f => f.CorrelationId).Select(g => {|LC024:g.OrderBy(f => f.Version).Distinct().Last()|});
            var unorderedFiltered = forms.GroupBy(f => f.CorrelationId).Select(g => {|LC024:g.Where(f => f.Version > 1).LastOrDefault()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
