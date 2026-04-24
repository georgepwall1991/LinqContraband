using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC030_DbContextInSingleton.DbContextInSingletonAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC030_DbContextInSingleton;

public class DbContextInSingletonTests
{
    private const string EFCoreMock = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext { }
    public class DbContextOptions<TContext> where TContext : DbContext { }
    public interface IDbContextFactory<TContext> where TContext : DbContext
    {
        TContext CreateDbContext();
    }
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

    private const string DependencyInjectionMock = @"
namespace Microsoft.Extensions.DependencyInjection
{
    using System;
    using Microsoft.EntityFrameworkCore;

    public enum ServiceLifetime
    {
        Singleton,
        Scoped,
        Transient
    }

    public interface IServiceCollection { }

    public sealed class ServiceCollection : IServiceCollection { }

    public static class ServiceCollectionServiceExtensions
    {
        public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
        public static IServiceCollection AddSingleton<TService, TImplementation>(this IServiceCollection services) where TImplementation : TService => services;
        public static IServiceCollection AddSingleton(this IServiceCollection services, Type serviceType) => services;
        public static IServiceCollection AddSingleton(this IServiceCollection services, Type serviceType, Type implementationType) => services;
        public static IServiceCollection AddScoped<TService>(this IServiceCollection services) => services;
        public static IServiceCollection AddTransient<TService>(this IServiceCollection services) => services;
        public static IServiceCollection AddHostedService<THostedService>(this IServiceCollection services) => services;
        public static IServiceCollection AddDbContext<TContext>(
            this IServiceCollection services,
            object optionsAction = null,
            ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
            ServiceLifetime optionsLifetime = ServiceLifetime.Scoped)
            where TContext : DbContext => services;
    }
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
    public async Task DbContextProperty_InBackgroundService_ShouldTriggerLC030()
    {
        var test = EFCoreMock + HostingMock + @"
public sealed class Worker : Microsoft.Extensions.Hosting.BackgroundService
{
    public Microsoft.EntityFrameworkCore.DbContext {|LC030:Db|} { get; set; }

    protected override System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken stoppingToken)
        => System.Threading.Tasks.Task.CompletedTask;
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
    public async Task DbContextConstructorParameter_InHostedServiceWithoutStoredMember_ShouldTriggerLC030()
    {
        var test = EFCoreMock + HostingMock + @"
public sealed class Worker : Microsoft.Extensions.Hosting.IHostedService
{
    public Worker(Microsoft.EntityFrameworkCore.DbContext {|LC030:db|}) { }

    public System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;
    public System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DbContextConstructorParameter_WithStoredMember_ShouldReportStoredMemberOnly()
    {
        var test = EFCoreMock + HostingMock + @"
public sealed class Worker : Microsoft.Extensions.Hosting.IHostedService
{
    private readonly Microsoft.EntityFrameworkCore.DbContext {|LC030:_db|};

    public Worker(Microsoft.EntityFrameworkCore.DbContext db) => _db = db;

    public System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;
    public System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AddSingletonRegisteredService_WithStoredDbContext_ShouldTriggerLC030()
    {
        var test = EFCoreMock + DependencyInjectionMock + @"
public sealed class Worker
{
    private readonly Microsoft.EntityFrameworkCore.DbContext {|LC030:_db|};
}

public static class Startup
{
    public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<Worker>(services);
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AddHostedServiceRegisteredService_WithStoredDbContext_ShouldTriggerLC030()
    {
        var test = EFCoreMock + DependencyInjectionMock + @"
public sealed class Worker
{
    private readonly Microsoft.EntityFrameworkCore.DbContext {|LC030:_db|};
}

public static class Startup
{
    public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddHostedService<Worker>(services);
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DirectDbContextAddSingleton_ShouldTriggerLC030()
    {
        var test = EFCoreMock + DependencyInjectionMock + @"
public sealed class AppDbContext : Microsoft.EntityFrameworkCore.DbContext { }

public static class Startup
{
    public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        {|LC030:Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<AppDbContext>(services)|};
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AddDbContext_WithSingletonLifetime_ShouldTriggerLC030()
    {
        var test = EFCoreMock + DependencyInjectionMock + @"
public sealed class AppDbContext : Microsoft.EntityFrameworkCore.DbContext { }

public static class Startup
{
    public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        {|LC030:Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddDbContext<AppDbContext>(services, contextLifetime: Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)|};
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AddDbContext_WithOnlySingletonOptionsLifetime_ShouldNotTrigger()
    {
        var test = EFCoreMock + DependencyInjectionMock + @"
public sealed class AppDbContext : Microsoft.EntityFrameworkCore.DbContext { }

public static class Startup
{
    public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddDbContext<AppDbContext>(services, optionsLifetime: Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton);
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NameOnlySingletonService_InStrictMode_ShouldNotTrigger()
    {
        var test = EFCoreMock + @"
public sealed class MySingletonService
{
    private readonly Microsoft.EntityFrameworkCore.DbContext _db;
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NameOnlySingletonService_InExpandedMode_ShouldTrigger()
    {
        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC030_DbContextInSingleton.DbContextInSingletonAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestCode = EFCoreMock + @"
public sealed class MySingletonService
{
    private readonly Microsoft.EntityFrameworkCore.DbContext {|LC030:_db|};
}
"
        };

        test.TestState.AnalyzerConfigFiles.Add(("/0/.editorconfig", """
root = true

[*.cs]
dotnet_code_quality.LC030.detection_mode = expanded
"""));

        await test.RunAsync();
    }

    [Fact]
    public async Task ConfiguredLongLivedInterface_ShouldTrigger()
    {
        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC030_DbContextInSingleton.DbContextInSingletonAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestCode = EFCoreMock + @"
namespace TestApp
{
    public interface ILongLivedWorker { }

    public sealed class Worker : ILongLivedWorker
    {
        private readonly Microsoft.EntityFrameworkCore.DbContext {|LC030:_db|};
    }
}
"
        };

        test.TestState.AnalyzerConfigFiles.Add(("/0/.editorconfig", """
root = true

[*.cs]
dotnet_code_quality.LC030.long_lived_types = TestApp.ILongLivedWorker
"""));

        await test.RunAsync();
    }

    [Fact]
    public async Task ScopedAndPerRequestTypes_ShouldNotTrigger()
    {
        var test = EFCoreMock + HttpMock + @"
public class MyController
{
    private readonly Microsoft.EntityFrameworkCore.DbContext _db;
}

public class MyPageModel
{
    private readonly Microsoft.EntityFrameworkCore.DbContext _db;
}

public class MyViewComponent
{
    private readonly Microsoft.EntityFrameworkCore.DbContext _db;
}

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

    [Fact]
    public async Task AddScopedAndAddTransientRegisteredServices_ShouldNotTrigger()
    {
        var test = EFCoreMock + DependencyInjectionMock + @"
public sealed class ScopedWorker
{
    private readonly Microsoft.EntityFrameworkCore.DbContext _db;
}

public sealed class TransientWorker
{
    private readonly Microsoft.EntityFrameworkCore.DbContext _db;
}

public static class Startup
{
    public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<ScopedWorker>(services);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddTransient<TransientWorker>(services);
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FactoryAndOptionsMembers_ShouldNotTrigger()
    {
        var test = EFCoreMock + HostingMock + @"
public sealed class Worker : Microsoft.Extensions.Hosting.IHostedService
{
    private readonly Microsoft.EntityFrameworkCore.IDbContextFactory<Microsoft.EntityFrameworkCore.DbContext> _factory;
    private readonly Microsoft.EntityFrameworkCore.DbContextOptions<Microsoft.EntityFrameworkCore.DbContext> _options;

    public System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;
    public System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AmbiguousServiceRegistration_ShouldNotTrigger()
    {
        var test = EFCoreMock + DependencyInjectionMock + @"
public interface IWorker { }

public sealed class Worker : IWorker
{
    private readonly Microsoft.EntityFrameworkCore.DbContext _db;
}

public static class Startup
{
    public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<IWorker>(services);
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
