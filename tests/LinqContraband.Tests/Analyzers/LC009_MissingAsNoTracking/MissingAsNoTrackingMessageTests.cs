using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using AnalyzerTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingAnalyzer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC009_MissingAsNoTracking;

// The message names the method the query sits in. A lambda has no name of its own, so the
// message used to read "Method '' appears to be read-only..." for a query inside Task.Run(() => ...).
public partial class MissingAsNoTrackingTests
{
    [Fact]
    public async Task Message_QueryInsideLambda_NamesEnclosingMethod()
    {
        var test = Usings + @"
class Program
{
    public void Run(MyDbContext db)
    {
        _ = Task.Run(() => {|#0:db.Users.ToList()|});
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test,
            VerifyCS.Diagnostic("LC009").WithLocation(0).WithArguments("Run"));
    }

    [Fact]
    public async Task Message_QueryInsideNestedLambdas_NamesEnclosingMethod()
    {
        var test = Usings + @"
class Program
{
    public void Load(MyDbContext db)
    {
        Func<Func<List<User>>> outer = () => () => {|#0:db.Users.Where(u => u.Id > 0).ToList()|};
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test,
            VerifyCS.Diagnostic("LC009").WithLocation(0).WithArguments("Load"));
    }

    [Fact]
    public async Task Message_QueryInsideLocalFunction_NamesLocalFunction()
    {
        var test = Usings + @"
class Program
{
    public void Load(MyDbContext db)
    {
        List<User> ReadUsers() => {|#0:db.Users.ToList()|};
        ReadUsers();
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test,
            VerifyCS.Diagnostic("LC009").WithLocation(0).WithArguments("ReadUsers"));
    }

    [Fact]
    public async Task Message_QueryInsidePropertyGetter_NamesProperty()
    {
        var test = Usings + @"
class Program
{
    private readonly MyDbContext _db = new MyDbContext();

    public List<User> AllUsers => {|#0:_db.Users.ToList()|};
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test,
            VerifyCS.Diagnostic("LC009").WithLocation(0).WithArguments("AllUsers"));
    }

    [Fact]
    public async Task Message_QueryInsideConstructor_NamesType()
    {
        var test = Usings + @"
class UserCache
{
    public UserCache(MyDbContext db)
    {
        var users = {|#0:db.Users.ToList()|};
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test,
            VerifyCS.Diagnostic("LC009").WithLocation(0).WithArguments("UserCache"));
    }

    [Fact]
    public async Task Message_QueryInTopLevelStatements_SaysTopLevelStatements()
    {
        var test = Usings + @"
var db = new MyDbContext();
var users = {|#0:db.Users.ToList()|};
" + MockNamespace;

        var analyzerTest = new AnalyzerTest { TestCode = test };
        analyzerTest.TestState.OutputKind = OutputKind.ConsoleApplication;
        analyzerTest.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC009", DiagnosticSeverity.Info)
                .WithLocation(0)
                .WithArguments("<top-level statements>"));

        await analyzerTest.RunAsync();
    }
}
