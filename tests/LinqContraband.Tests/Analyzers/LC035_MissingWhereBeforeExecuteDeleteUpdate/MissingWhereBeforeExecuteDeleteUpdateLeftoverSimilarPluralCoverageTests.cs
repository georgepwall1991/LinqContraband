using VerifyCS = LinqContraband.Tests.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate.MissingWhereBeforeExecuteDeleteUpdateVerifier;

namespace LinqContraband.Tests.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate;

/// <summary>
/// Remaining leftover similar plurals that the shipped
/// <c>Leaf</c>/<c>Criterion</c>/<c>Radius</c>/<c>Medium</c>/<c>Knife</c>/
/// <c>Quiz</c> fixtures do not isolate, and that leftover drafts do not
/// pin. Does not restage
/// <c>MissingWhereBeforeExecuteDeleteUpdateLeftoverPluralCoverageTests</c>
/// or
/// <c>MissingWhereBeforeExecuteDeleteUpdateLeftoverClassicalPluralCoverageTests</c>.
/// Dropping one leftover pair fails only that quiet pin. Setting
/// <c>AlsoRegular</c> to false fails only the regular spelling pin.
/// Helper is <c>LeftoverSimilarClear</c> so it does not clash with
/// leftover drafts' <c>LeftoverClear</c> / <c>LeftoverClassicalClear</c>.
/// </summary>
public partial class MissingWhereBeforeExecuteDeleteUpdateTests
{
    private static string LeftoverSimilarClear(string entity, string methodName, bool reports) =>
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

    private static Task VerifyLeftoverSimilarClear(string entity, string methodName, bool reports) =>
        VerifyCS.VerifyAnalyzerAsync(LeftoverSimilarClear(entity, methodName, reports));

    [Fact]
    public Task Dwarf_Dwarfs_StaysQuiet() =>
        VerifyLeftoverSimilarClear("Dwarf", "ClearDwarfs", reports: false);

    [Fact]
    public Task Wharf_Wharves_StaysQuiet() =>
        VerifyLeftoverSimilarClear("Wharf", "ClearWharves", reports: false);

    [Fact]
    public Task Wharf_Wharfs_StaysQuiet() =>
        VerifyLeftoverSimilarClear("Wharf", "ClearWharfs", reports: false);

    [Fact]
    public Task Nucleus_Nuclei_StaysQuiet() =>
        VerifyLeftoverSimilarClear("Nucleus", "ClearNuclei", reports: false);

    [Fact]
    public Task Nucleus_Nucleuses_StaysQuiet() =>
        VerifyLeftoverSimilarClear("Nucleus", "ClearNucleuses", reports: false);

    [Fact]
    public Task Stimulus_Stimuli_StaysQuiet() =>
        VerifyLeftoverSimilarClear("Stimulus", "ClearStimuli", reports: false);

    [Fact]
    public Task Stimulus_Stimuluses_StaysQuiet() =>
        VerifyLeftoverSimilarClear("Stimulus", "ClearStimuluses", reports: false);

    [Fact]
    public Task Alumnus_Alumni_StaysQuiet() =>
        VerifyLeftoverSimilarClear("Alumnus", "ClearAlumni", reports: false);

    [Fact]
    public Task Alumnus_Alumnuses_StaysQuiet() =>
        VerifyLeftoverSimilarClear("Alumnus", "ClearAlumnuses", reports: false);

    [Fact]
    public Task Syllabus_Syllabi_StaysQuiet() =>
        VerifyLeftoverSimilarClear("Syllabus", "ClearSyllabi", reports: false);

    [Fact]
    public Task Syllabus_Syllabuses_StaysQuiet() =>
        VerifyLeftoverSimilarClear("Syllabus", "ClearSyllabuses", reports: false);

    [Fact]
    public Task Focus_Foci_StaysQuiet() =>
        VerifyLeftoverSimilarClear("Focus", "ClearFoci", reports: false);

    [Fact]
    public Task Focus_Focuses_StaysQuiet() =>
        VerifyLeftoverSimilarClear("Focus", "ClearFocuses", reports: false);

    [Fact]
    public Task Memorandum_Memoranda_StaysQuiet() =>
        VerifyLeftoverSimilarClear("Memorandum", "ClearMemoranda", reports: false);

    [Fact]
    public Task Memorandum_Memorandums_StaysQuiet() =>
        VerifyLeftoverSimilarClear("Memorandum", "ClearMemorandums", reports: false);

    [Fact]
    public Task LookalikeWharves_OnPerson_StillReports() =>
        VerifyLeftoverSimilarClear("Person", "ClearWharves", reports: true);
}
