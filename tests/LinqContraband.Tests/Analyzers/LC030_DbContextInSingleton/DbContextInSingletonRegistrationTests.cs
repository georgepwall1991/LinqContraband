using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC030_DbContextInSingleton.DbContextInSingletonAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC030_DbContextInSingleton;

public partial class DbContextInSingletonTests
{
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
    public async Task AddSingletonServiceImplementation_WithStoredDbContext_ShouldTriggerLC030()
    {
        var test = EFCoreMock + DependencyInjectionMock + @"
public interface IWorker { }

public sealed class Worker : IWorker
{
    private readonly Microsoft.EntityFrameworkCore.DbContext {|LC030:_db|};
}

public static class Startup
{
    public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<IWorker, Worker>(services);
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AddSingletonFactoryImplementation_WithStoredDbContext_ShouldTriggerLC030()
    {
        var test = EFCoreMock + DependencyInjectionMock + @"
public interface IWorker { }

public sealed class Worker : IWorker
{
    private readonly Microsoft.EntityFrameworkCore.DbContext {|LC030:_db|};

    public Worker(Microsoft.EntityFrameworkCore.DbContext db) => _db = db;
}

public static class Startup
{
    public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<IWorker>(services, _ => new Worker(default!));
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AddSingletonTypeImplementation_WithStoredDbContext_ShouldTriggerLC030()
    {
        var test = EFCoreMock + DependencyInjectionMock + @"
public interface IWorker { }

public sealed class Worker : IWorker
{
    private readonly Microsoft.EntityFrameworkCore.DbContext {|LC030:_db|};
}

public static class Startup
{
    public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, typeof(IWorker), typeof(Worker));
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AddSingletonInDependencyInjectionNamespaceWithoutServiceCollection_ShouldNotTrigger()
    {
        var test = EFCoreMock + @"
namespace Microsoft.Extensions.DependencyInjection
{
    public sealed class NotServices { }

    public static class CustomExtensions
    {
        public static NotServices AddSingleton<TService>(this NotServices services) => services;
    }
}

public sealed class Worker
{
    private readonly Microsoft.EntityFrameworkCore.DbContext _db;
}

public static class Startup
{
    public static void Configure(Microsoft.Extensions.DependencyInjection.NotServices services)
    {
        Microsoft.Extensions.DependencyInjection.CustomExtensions.AddSingleton<Worker>(services);
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MultipleStoredDbContexts_ShouldReportInSourceOrder()
    {
        var test = EFCoreMock + DependencyInjectionMock + @"
public sealed class Worker
{
    private readonly Microsoft.EntityFrameworkCore.DbContext {|LC030:_first|};
    private readonly Microsoft.EntityFrameworkCore.DbContext {|LC030:_second|};
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
