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
    public async Task Fixer_ShouldNotRegister_ForAdvisoryLifetimeDiagnostic()
    {
        var test = EFCoreMock + @"
namespace Microsoft.Extensions.Hosting
{
    using System.Threading;
    using System.Threading.Tasks;

    public interface IHostedService
    {
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
    }
}

public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext { }

public sealed class Worker : Microsoft.Extensions.Hosting.IHostedService
{
    private readonly AppDbContext _db;

    public System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;
    public System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;
}
";

        var expected = CodeFixVerifier.Diagnostic("LC030").WithSpan(23, 35, 23, 38).WithArguments("Worker", "_db");
        await CodeFixVerifier.VerifyCodeFixAsync(test, expected, test);
    }
}
