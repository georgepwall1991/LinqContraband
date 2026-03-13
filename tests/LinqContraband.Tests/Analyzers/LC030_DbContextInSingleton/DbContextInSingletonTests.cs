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

    private const string HostingMock = @"
namespace Microsoft.Extensions.Hosting
{
    using System.Threading;
    using System.Threading.Tasks;

    public interface IHostedService
    {
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
    }

    public abstract class BackgroundService : IHostedService
    {
        public virtual Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public virtual Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        protected abstract Task ExecuteAsync(CancellationToken stoppingToken);
    }
}
";

    private const string HttpMock = @"
namespace Microsoft.AspNetCore.Http
{
    using System.Threading.Tasks;

    public sealed class HttpContext { }

    public interface IMiddleware
    {
        Task InvokeAsync(HttpContext context, RequestDelegate next);
    }

    public delegate Task RequestDelegate(HttpContext context);
}
";

    [Fact]
    public async Task DbContextField_InHostedService_ShouldTriggerLC030()
    {
        var test = EFCoreMock + HostingMock + @"
public sealed class Worker : Microsoft.Extensions.Hosting.IHostedService
{
    private readonly Microsoft.EntityFrameworkCore.DbContext {|LC030:_db|};

    public System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;
    public System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DbContextProperty_InConventionalMiddleware_ShouldTriggerLC030()
    {
        var test = EFCoreMock + HttpMock + @"
public sealed class AuditMiddleware
{
    public Microsoft.EntityFrameworkCore.DbContext {|LC030:Db|} { get; set; }

    public System.Threading.Tasks.Task InvokeAsync(Microsoft.AspNetCore.Http.HttpContext context)
        => System.Threading.Tasks.Task.CompletedTask;
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DbContext_InGenericClass_ShouldNotTrigger()
    {
        var test = EFCoreMock + @"
public class MyService
{
    private readonly Microsoft.EntityFrameworkCore.DbContext _db;
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
    public async Task DbContext_InIMiddlewareImplementation_ShouldNotTrigger()
    {
        var test = EFCoreMock + HttpMock + @"
public sealed class ScopedAuditMiddleware : Microsoft.AspNetCore.Http.IMiddleware
{
    private readonly Microsoft.EntityFrameworkCore.DbContext _db;

    public System.Threading.Tasks.Task InvokeAsync(Microsoft.AspNetCore.Http.HttpContext context, Microsoft.AspNetCore.Http.RequestDelegate next)
        => System.Threading.Tasks.Task.CompletedTask;
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
