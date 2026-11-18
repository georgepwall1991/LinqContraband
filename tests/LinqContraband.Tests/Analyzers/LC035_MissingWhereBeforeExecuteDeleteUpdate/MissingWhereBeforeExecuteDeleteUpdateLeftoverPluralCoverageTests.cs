using VerifyCS = LinqContraband.Tests.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate.MissingWhereBeforeExecuteDeleteUpdateVerifier;

namespace LinqContraband.Tests.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate;

/// <summary>
/// Leftover irregular, invariant and classical plurals that the shipped
/// <c>Person</c>/<c>Series</c>/<c>Leaf</c>/<c>Criterion</c>/<c>Radius</c>/
/// <c>Medium</c>/<c>Knife</c>/<c>Quiz</c> fixtures do not isolate.
/// Dropping one leftover pair fails only that quiet pin. <c>Woman</c>/<c>Women</c>
/// is not isolatable: <c>Woman.EndsWith("Man")</c> matches <c>Man</c> first and
/// still yields <c>Women</c>. Invariant <c>Data</c> shares <c>WholeSetNouns</c>
/// and classical <c>Datum</c> and is not locked. Remaining classical leftovers
/// (<c>Thief</c>, <c>Wife</c>, <c>Fungus</c>, <c>Curriculum</c> and similar)
/// stay unpinned. <c>ClearWhizes</c> is not locked as a positive: dropping
/// <c>Whiz</c> makes the regular <c>z+es</c> spelling quiet. Does not restage
/// <c>MissingWhereBeforeExecuteDeleteUpdateNamedIntentTests.cs</c>.
/// </summary>
public partial class MissingWhereBeforeExecuteDeleteUpdateTests
{
    private static string LeftoverClear(string entity, string methodName, bool reports) =>
        "using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class " + entity + @" { public int Id { get; set; } }

    public class Maintenance
    {
        public int " + methodName + @"(DbContext db) => " +
        (reports ? "{|LC035:" : "") + "db.Set<" + entity + ">().ExecuteDelete()" +
        (reports ? "|}" : "") + @";
    }
}";

    private static Task VerifyLeftoverClear(string entity, string methodName, bool reports) =>
        VerifyCS.VerifyAnalyzerAsync(LeftoverClear(entity, methodName, reports));

    [Fact]
    public Task Child_Children_StaysQuiet() =>
        VerifyLeftoverClear("Child", "ClearChildren", reports: false);

    [Fact]
    public Task Man_Men_StaysQuiet() =>
        VerifyLeftoverClear("Man", "ClearMen", reports: false);

    [Fact]
    public Task Mouse_Mice_StaysQuiet() =>
        VerifyLeftoverClear("Mouse", "ClearMice", reports: false);

    [Fact]
    public Task Goose_Geese_StaysQuiet() =>
        VerifyLeftoverClear("Goose", "ClearGeese", reports: false);

    [Fact]
    public Task Tooth_Teeth_StaysQuiet() =>
        VerifyLeftoverClear("Tooth", "ClearTeeth", reports: false);

    [Fact]
    public Task Foot_Feet_StaysQuiet() =>
        VerifyLeftoverClear("Foot", "ClearFeet", reports: false);

    [Fact]
    public Task Species_StaysQuiet() =>
        VerifyLeftoverClear("Species", "ClearSpecies", reports: false);

    [Fact]
    public Task Sheep_StaysQuiet() =>
        VerifyLeftoverClear("Sheep", "ClearSheep", reports: false);

    [Fact]
    public Task Fish_StaysQuiet() =>
        VerifyLeftoverClear("Fish", "ClearFish", reports: false);

    [Fact]
    public Task Deer_StaysQuiet() =>
        VerifyLeftoverClear("Deer", "ClearDeer", reports: false);

    [Fact]
    public Task News_StaysQuiet() =>
        VerifyLeftoverClear("News", "ClearNews", reports: false);

    [Fact]
    public Task Loaf_Loaves_StaysQuiet() =>
        VerifyLeftoverClear("Loaf", "ClearLoaves", reports: false);

    [Fact]
    public Task Scarf_Scarves_StaysQuiet() =>
        VerifyLeftoverClear("Scarf", "ClearScarves", reports: false);

    [Fact]
    public Task Scarf_Scarfs_StaysQuiet() =>
        VerifyLeftoverClear("Scarf", "ClearScarfs", reports: false);

    [Fact]
    public Task Cactus_Cacti_StaysQuiet() =>
        VerifyLeftoverClear("Cactus", "ClearCacti", reports: false);

    [Fact]
    public Task Bacterium_Bacteria_StaysQuiet() =>
        VerifyLeftoverClear("Bacterium", "ClearBacteria", reports: false);

    [Fact]
    public Task Phenomenon_Phenomena_StaysQuiet() =>
        VerifyLeftoverClear("Phenomenon", "ClearPhenomena", reports: false);

    [Fact]
    public Task Whiz_Whizzes_StaysQuiet() =>
        VerifyLeftoverClear("Whiz", "ClearWhizzes", reports: false);

    [Fact]
    public Task LookalikeChildren_OnPerson_StillReports() =>
        VerifyLeftoverClear("Person", "ClearChildren", reports: true);
}
