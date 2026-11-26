using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// Leftover LanguageExt sequence-form names the shipped <c>ToSeq</c> / <c>RightToSeq</c> /
/// <c>LeftToList</c> fixtures do not isolate. Dropping leftover <c>ToList</c> from the
/// Option arm fails only that quiet pin; the shipped <c>ToSeq</c> row stays green.
/// Dropping leftover <c>LeftToSeq</c> fails only that pin; <c>RightToSeq</c> stays green.
/// Dropping <c>EitherUnsafe</c> fails only that type pin; <c>Either.RightToSeq</c> stays green.
/// A lookalike leftover name still reports so a later edit that treats every matching
/// name as LanguageExt is visible. <c>OptionUnsafe</c> foreach is owned by draft #711
/// and is not restaged.
/// </summary>
public partial class NPlusOneLooperTests
{
    [Fact]
    public Task Option_ToList_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"LanguageExt.Option<int> leftoverId = default;
        foreach (var id in leftoverId.ToList())
        {
            Handle(await db.Users.Where(u => u.ParentId == id).ToListAsync());
        }"));

    [Fact]
    public Task Option_ToArray_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"LanguageExt.Option<int> leftoverId = default;
        foreach (var id in leftoverId.ToArray())
        {
            Handle(await db.Users.Where(u => u.ParentId == id).ToListAsync());
        }"));

    [Fact]
    public Task Option_AsEnumerable_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"LanguageExt.Option<int> leftoverId = default;
        foreach (var id in leftoverId.AsEnumerable())
        {
            Handle(await db.Users.Where(u => u.ParentId == id).ToListAsync());
        }"));

    [Fact]
    public Task Either_LeftToSeq_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"LanguageExt.Either<int, string> leftoverId = default;
        foreach (var id in leftoverId.LeftToSeq())
        {
            Handle(await db.Users.Where(u => u.ParentId == id).ToListAsync());
        }"));

    [Fact]
    public Task Either_RightToList_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"LanguageExt.Either<string, int> leftoverId = default;
        foreach (var id in leftoverId.RightToList())
        {
            Handle(await db.Users.Where(u => u.ParentId == id).ToListAsync());
        }"));

    [Fact]
    public Task Either_RightToArray_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"LanguageExt.Either<string, int> leftoverId = default;
        foreach (var id in leftoverId.RightToArray())
        {
            Handle(await db.Users.Where(u => u.ParentId == id).ToListAsync());
        }"));

    [Fact]
    public Task Either_LeftToArray_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"LanguageExt.Either<int, string> leftoverId = default;
        foreach (var id in leftoverId.LeftToArray())
        {
            Handle(await db.Users.Where(u => u.ParentId == id).ToListAsync());
        }"));

    [Fact]
    public Task Either_RightAsEnumerable_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"LanguageExt.Either<string, int> leftoverId = default;
        foreach (var id in leftoverId.RightAsEnumerable())
        {
            Handle(await db.Users.Where(u => u.ParentId == id).ToListAsync());
        }"));

    [Fact]
    public Task Either_LeftAsEnumerable_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"LanguageExt.Either<int, string> leftoverId = default;
        foreach (var id in leftoverId.LeftAsEnumerable())
        {
            Handle(await db.Users.Where(u => u.ParentId == id).ToListAsync());
        }"));

    [Fact]
    public Task Either_ToSeq_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"LanguageExt.Either<string, int> leftoverId = default;
        foreach (var id in leftoverId.ToSeq())
        {
            Handle(await db.Users.Where(u => u.ParentId == id).ToListAsync());
        }"));

    [Fact]
    public Task Either_ToList_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"LanguageExt.Either<string, int> leftoverId = default;
        foreach (var id in leftoverId.ToList())
        {
            Handle(await db.Users.Where(u => u.ParentId == id).ToListAsync());
        }"));

    [Fact]
    public Task Either_ToArray_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"LanguageExt.Either<string, int> leftoverId = default;
        foreach (var id in leftoverId.ToArray())
        {
            Handle(await db.Users.Where(u => u.ParentId == id).ToListAsync());
        }"));

    [Fact]
    public Task Either_AsEnumerable_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"LanguageExt.Either<string, int> leftoverId = default;
        foreach (var id in leftoverId.AsEnumerable())
        {
            Handle(await db.Users.Where(u => u.ParentId == id).ToListAsync());
        }"));

    [Fact]
    public Task EitherUnsafe_RightToSeq_StaysQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"LanguageExt.EitherUnsafe<string, int> leftoverUnsafe = default;
        foreach (var id in leftoverUnsafe.RightToSeq())
        {
            Handle(await db.Users.Where(u => u.ParentId == id).ToListAsync());
        }"));

    [Fact]
    public Task LookalikeEither_LeftToSeq_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"Lookalike.Either<int, string> leftoverId = default;
        foreach (var id in leftoverId.LeftToSeq())
        {
            Handle(await {|LC007:db.Users.Where(u => u.ParentId == id).ToListAsync()|});
        }"));

    [Fact]
    public Task LookalikeEitherUnsafe_RightToSeq_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(@"Lookalike.EitherUnsafe<string, int> leftoverUnsafe = default;
        foreach (var id in leftoverUnsafe.RightToSeq())
        {
            Handle(await {|LC007:db.Users.Where(u => u.ParentId == id).ToListAsync()|});
        }"));
}
