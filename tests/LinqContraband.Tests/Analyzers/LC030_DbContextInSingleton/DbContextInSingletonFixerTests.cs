using CodeFixVerifier = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC030_DbContextInSingleton.DbContextInSingletonAnalyzer,
    LinqContraband.Analyzers.LC030_DbContextInSingleton.DbContextInSingletonFixer>;

namespace LinqContraband.Tests.Analyzers.LC030_DbContextInSingleton;

public class DbContextInSingletonFixerTests
{
    private const string EFCoreMock = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext { }
}
";

    [Fact]
    public async Task Fixer_ShouldNotRegister_ForHeuristicLifetimeDiagnostic()
    {
        var test = EFCoreMock + @"
public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext { }

public class MyService
{
    private readonly AppDbContext _db;
}
";

        var expected = CodeFixVerifier.Diagnostic("LC030").WithSpan(11, 35, 11, 38).WithArguments("MyService", "_db");
        await CodeFixVerifier.VerifyCodeFixAsync(test, expected, test);
    }
}
