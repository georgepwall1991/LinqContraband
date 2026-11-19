using VerifyCS = LinqContraband.Tests.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate.MissingWhereBeforeExecuteDeleteUpdateVerifier;

namespace LinqContraband.Tests.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate;

/// <summary>
/// Remaining leftover classical plurals that the shipped
/// <c>Leaf</c>/<c>Criterion</c>/<c>Radius</c>/<c>Medium</c>/<c>Knife</c>/
/// <c>Quiz</c> fixtures do not isolate. Does not restage
/// <c>MissingWhereBeforeExecuteDeleteUpdateLeftoverPluralCoverageTests</c>
/// or the leftover irregular/invariant pins.
/// Dropping one leftover pair fails only that quiet pin. Setting
/// <c>AlsoRegular</c> to false fails only the regular spelling pin.
/// Remaining similar leftovers (<c>Dwarf</c> regular, <c>Wharf</c>,
/// <c>Nucleus</c>, <c>Stimulus</c>, <c>Alumnus</c>, <c>Syllabus</c>,
/// <c>Focus</c>, <c>Memorandum</c>) stay unpinned. Regular-only
/// spellings of listed non-<c>AlsoRegular</c> words
/// (<c>ClearThiefs</c>, <c>ClearWifes</c>) are not locked as positives:
/// dropping the listing makes the regular plural quiet.
/// </summary>
public partial class MissingWhereBeforeExecuteDeleteUpdateTests
{
    private static string LeftoverClassicalClear(string entity, string methodName, bool reports) =>
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

    private static Task VerifyLeftoverClassicalClear(string entity, string methodName, bool reports) =>
        VerifyCS.VerifyAnalyzerAsync(LeftoverClassicalClear(entity, methodName, reports));

    [Fact]
    public Task Thief_Thieves_StaysQuiet() =>
        VerifyLeftoverClassicalClear("Thief", "ClearThieves", reports: false);

    [Fact]
    public Task Sheaf_Sheaves_StaysQuiet() =>
        VerifyLeftoverClassicalClear("Sheaf", "ClearSheaves", reports: false);

    [Fact]
    public Task Half_Halves_StaysQuiet() =>
        VerifyLeftoverClassicalClear("Half", "ClearHalves", reports: false);

    [Fact]
    public Task Calf_Calves_StaysQuiet() =>
        VerifyLeftoverClassicalClear("Calf", "ClearCalves", reports: false);

    [Fact]
    public Task Shelf_Shelves_StaysQuiet() =>
        VerifyLeftoverClassicalClear("Shelf", "ClearShelves", reports: false);

    [Fact]
    public Task Wolf_Wolves_StaysQuiet() =>
        VerifyLeftoverClassicalClear("Wolf", "ClearWolves", reports: false);

    [Fact]
    public Task Self_Selves_StaysQuiet() =>
        VerifyLeftoverClassicalClear("Self", "ClearSelves", reports: false);

    [Fact]
    public Task Elf_Elves_StaysQuiet() =>
        VerifyLeftoverClassicalClear("Elf", "ClearElves", reports: false);

    [Fact]
    public Task Wife_Wives_StaysQuiet() =>
        VerifyLeftoverClassicalClear("Wife", "ClearWives", reports: false);

    [Fact]
    public Task Life_Lives_StaysQuiet() =>
        VerifyLeftoverClassicalClear("Life", "ClearLives", reports: false);

    [Fact]
    public Task Hoof_Hooves_StaysQuiet() =>
        VerifyLeftoverClassicalClear("Hoof", "ClearHooves", reports: false);

    [Fact]
    public Task Hoof_Hoofs_StaysQuiet() =>
        VerifyLeftoverClassicalClear("Hoof", "ClearHoofs", reports: false);

    [Fact]
    public Task Fungus_Fungi_StaysQuiet() =>
        VerifyLeftoverClassicalClear("Fungus", "ClearFungi", reports: false);

    [Fact]
    public Task Fungus_Funguses_StaysQuiet() =>
        VerifyLeftoverClassicalClear("Fungus", "ClearFunguses", reports: false);

    [Fact]
    public Task Curriculum_Curricula_StaysQuiet() =>
        VerifyLeftoverClassicalClear("Curriculum", "ClearCurricula", reports: false);

    [Fact]
    public Task Curriculum_Curriculums_StaysQuiet() =>
        VerifyLeftoverClassicalClear("Curriculum", "ClearCurriculums", reports: false);

    [Fact]
    public Task Stratum_Strata_StaysQuiet() =>
        VerifyLeftoverClassicalClear("Stratum", "ClearStrata", reports: false);

    [Fact]
    public Task Dwarf_Dwarves_StaysQuiet() =>
        VerifyLeftoverClassicalClear("Dwarf", "ClearDwarves", reports: false);

    [Fact]
    public Task LookalikeThieves_OnPerson_StillReports() =>
        VerifyLeftoverClassicalClear("Person", "ClearThieves", reports: true);
}
