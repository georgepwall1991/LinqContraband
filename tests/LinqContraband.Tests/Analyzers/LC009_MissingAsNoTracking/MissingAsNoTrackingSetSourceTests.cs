using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC009_MissingAsNoTracking;

public partial class MissingAsNoTrackingTests
{
    [Fact]
    public async Task TestCrime_SetGenericSource_ReadOnly_TriggersDiagnostic()
    {
        // The generic-repository read path: context.Set<T>() returns a DbSet, so a
        // read-only materialization with no AsNoTracking should be flagged just like db.Users.
        var test = Usings + @"
class Program
{
    public List<User> GetUsers()
    {
        var db = new MyDbContext();
        return {|LC009:db.Set<User>().ToList()|};
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_SetGenericSource_WithWhere_TriggersDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public List<User> GetUsers()
    {
        var db = new MyDbContext();
        return {|LC009:db.Set<User>().Where(u => u.Id > 0).ToList()|};
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_SetGenericSource_WithAsNoTracking_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public List<User> GetUsers()
    {
        var db = new MyDbContext();
        return db.Set<User>().AsNoTracking().Where(u => u.Id > 0).ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_SetGenericSource_WithSelect_NoDiagnostic()
    {
        var test = Usings + @"
class Program
{
    public List<UserDto> GetDtos()
    {
        var db = new MyDbContext();
        return db.Set<User>().Select(u => new UserDto { Id = u.Id }).ToList();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
