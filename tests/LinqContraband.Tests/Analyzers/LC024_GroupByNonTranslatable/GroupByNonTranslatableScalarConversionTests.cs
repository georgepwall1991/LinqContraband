using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC024_GroupByNonTranslatable.GroupByNonTranslatableAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC024_GroupByNonTranslatable;

// Bitwarden folds a group's bool flags with `Convert.ToBoolean(g.Min(c => Convert.ToInt32(c.ReadOnly)))`,
// which EF Core's relational providers translate to MIN over a CAST; a scan reported it four times.
public partial class GroupByNonTranslatableTests
{
    private const string AccessModel = @"
namespace TestApp
{
    public class CollectionAccess
    {
        public int CollectionId { get; set; }
        public bool ReadOnly { get; set; }
        public bool Manage { get; set; }
        public decimal Amount { get; set; }
    }
";

    [Fact]
    public async Task GroupBy_ConvertAroundAggregateOfConvertedFlag_ShouldNotTrigger()
    {
        var test = Usings + AccessModel + @"
    public class TestClass
    {
        public void TestMethod(IQueryable<CollectionAccess> access)
        {
            var result = access
                .GroupBy(a => a.CollectionId)
                .Select(g => new
                {
                    g.Key,
                    ReadOnly = Convert.ToBoolean(g.Min(c => Convert.ToInt32(c.ReadOnly))),
                    Manage = Convert.ToBoolean(g.Max(c => Convert.ToInt32(c.Manage)))
                });
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBy_QuerySyntaxConvertAroundAggregate_ShouldNotTrigger()
    {
        var test = Usings + AccessModel + @"
    public class TestClass
    {
        public void TestMethod(IQueryable<CollectionAccess> access)
        {
            var result = from a in access
                         group a by a.CollectionId into collectionGroup
                         select new
                         {
                             Id = collectionGroup.Key,
                             ReadOnly = Convert.ToBoolean(collectionGroup.Min(c => Convert.ToInt32(c.ReadOnly)))
                         };
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBy_MathAroundAggregate_ShouldNotTrigger()
    {
        var test = Usings + AccessModel + @"
    public class TestClass
    {
        public void TestMethod(IQueryable<CollectionAccess> access)
        {
            var result = access
                .GroupBy(a => a.CollectionId)
                .Select(g => new { g.Key, Average = Math.Round(g.Average(c => c.Amount), 2) });
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBy_ConvertAroundElementAccess_ShouldTrigger()
    {
        var test = Usings + AccessModel + @"
    public class TestClass
    {
        public void TestMethod(IQueryable<CollectionAccess> access)
        {
            var result = access
                .GroupBy(a => a.CollectionId)
                .Select(g => new { g.Key, ReadOnly = {|LC024:Convert.ToBoolean(g.First().ReadOnly)|} });
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBy_UserMethodInsideAggregateSelector_ShouldTrigger()
    {
        var test = Usings + AccessModel + @"
    public static class Flags
    {
        public static int ToFlag(bool value) => value ? 1 : 0;
    }

    public class TestClass
    {
        public void TestMethod(IQueryable<CollectionAccess> access)
        {
            var result = access
                .GroupBy(a => a.CollectionId)
                .Select(g => new { g.Key, ReadOnly = {|LC024:g.Min(c => Flags.ToFlag(c.ReadOnly))|} });
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
