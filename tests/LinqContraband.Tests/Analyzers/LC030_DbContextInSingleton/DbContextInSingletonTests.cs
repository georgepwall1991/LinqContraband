using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC030_DbContextInSingleton.DbContextInSingletonAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC030_DbContextInSingleton;

public class DbContextInSingletonTests
{
    private const string EFCoreMock = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext { }
}
";

    [Fact]
    public async Task DbContextField_InGenericClass_ShouldTriggerLC030()
    {
        var test = EFCoreMock + @"
public class MyService
{
    private readonly Microsoft.EntityFrameworkCore.DbContext {|LC030:_db|};
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DbContextProperty_InGenericClass_ShouldTriggerLC030()
    {
        var test = EFCoreMock + @"
public class MyService
{
    public Microsoft.EntityFrameworkCore.DbContext {|LC030:Db|} { get; set; }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DbContext_InController_ShouldNotTrigger()
    {
        var test = EFCoreMock + @"
public class MyController
{
    private readonly Microsoft.EntityFrameworkCore.DbContext _db;
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DbContext_InDerivedDbContext_ShouldNotTrigger()
    {
        var test = EFCoreMock + @"
public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    private readonly Microsoft.EntityFrameworkCore.DbContext _inner;
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
