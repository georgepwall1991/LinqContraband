using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC010_SaveChangesInLoop.SaveChangesInLoopAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC010_SaveChangesInLoop;

/// <summary>
/// Leftover LanguageExt sequence-form names the shipped LC010 <c>ToSeq</c> /
/// <c>RightToSeq</c> fixtures do not isolate. The names live on the shared
/// <c>LoopIterationExtensions</c> helper. Dropping leftover <c>ToList</c> from
/// the Option arm fails only that quiet pin; the shipped <c>ToSeq</c> row stays
/// green. Dropping leftover <c>LeftToSeq</c> fails only that pin; <c>RightToSeq</c>
/// stays green. Dropping <c>EitherUnsafe</c> fails only that type pin.
/// A lookalike leftover name still reports. <c>OptionUnsafe</c> foreach is owned
/// by draft #711 and is not restaged. Locals are named <c>leftoverId</c> /
/// <c>leftoverUnsafe</c> so they do not clash with <c>Program</c>'s <c>maybeId</c>.
/// </summary>
public partial class SaveChangesInLoopSinglePassTests
{
    [Fact]
    public Task Option_ToList_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"LanguageExt.Option<int> leftoverId = default;
        foreach (var id in leftoverId.ToList())
        {
            db.SaveChanges();
        }"));

    [Fact]
    public Task Option_ToArray_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"LanguageExt.Option<int> leftoverId = default;
        foreach (var id in leftoverId.ToArray())
        {
            db.SaveChanges();
        }"));

    [Fact]
    public Task Option_AsEnumerable_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"LanguageExt.Option<int> leftoverId = default;
        foreach (var id in leftoverId.AsEnumerable())
        {
            db.SaveChanges();
        }"));

    [Fact]
    public Task Either_LeftToSeq_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"LanguageExt.Either<int, string> leftoverId = default;
        foreach (var id in leftoverId.LeftToSeq())
        {
            db.SaveChanges();
        }"));

    [Fact]
    public Task Either_LeftToList_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"LanguageExt.Either<int, string> leftoverId = default;
        foreach (var id in leftoverId.LeftToList())
        {
            db.SaveChanges();
        }"));

    [Fact]
    public Task Either_RightToList_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"LanguageExt.Either<string, int> leftoverId = default;
        foreach (var id in leftoverId.RightToList())
        {
            db.SaveChanges();
        }"));

    [Fact]
    public Task Either_RightToArray_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"LanguageExt.Either<string, int> leftoverId = default;
        foreach (var id in leftoverId.RightToArray())
        {
            db.SaveChanges();
        }"));

    [Fact]
    public Task Either_LeftToArray_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"LanguageExt.Either<int, string> leftoverId = default;
        foreach (var id in leftoverId.LeftToArray())
        {
            db.SaveChanges();
        }"));

    [Fact]
    public Task Either_RightAsEnumerable_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"LanguageExt.Either<string, int> leftoverId = default;
        foreach (var id in leftoverId.RightAsEnumerable())
        {
            db.SaveChanges();
        }"));

    [Fact]
    public Task Either_LeftAsEnumerable_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"LanguageExt.Either<int, string> leftoverId = default;
        foreach (var id in leftoverId.LeftAsEnumerable())
        {
            db.SaveChanges();
        }"));

    [Fact]
    public Task Either_ToSeq_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"LanguageExt.Either<string, int> leftoverId = default;
        foreach (var id in leftoverId.ToSeq())
        {
            db.SaveChanges();
        }"));

    [Fact]
    public Task Either_ToList_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"LanguageExt.Either<string, int> leftoverId = default;
        foreach (var id in leftoverId.ToList())
        {
            db.SaveChanges();
        }"));

    [Fact]
    public Task Either_ToArray_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"LanguageExt.Either<string, int> leftoverId = default;
        foreach (var id in leftoverId.ToArray())
        {
            db.SaveChanges();
        }"));

    [Fact]
    public Task Either_AsEnumerable_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"LanguageExt.Either<string, int> leftoverId = default;
        foreach (var id in leftoverId.AsEnumerable())
        {
            db.SaveChanges();
        }"));

    [Fact]
    public Task EitherUnsafe_RightToSeq_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"LanguageExt.EitherUnsafe<string, int> leftoverUnsafe = default;
        foreach (var id in leftoverUnsafe.RightToSeq())
        {
            db.SaveChanges();
        }"));

    [Fact]
    public Task LookalikeEither_LeftToSeq_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"Lookalike.Either<int, string> leftoverId = default;
        foreach (var id in leftoverId.LeftToSeq())
        {
            {|LC010:db.SaveChanges()|};
        }"));

    [Fact]
    public Task LookalikeEitherUnsafe_RightToSeq_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"Lookalike.EitherUnsafe<string, int> leftoverUnsafe = default;
        foreach (var id in leftoverUnsafe.RightToSeq())
        {
            {|LC010:db.SaveChanges()|};
        }"));
}
