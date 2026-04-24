# Spec: LC030 - Review DbContext Lifetime Mismatches

## Goal
Flag proven long-lived types that store or directly receive a `DbContext`, and flag explicit singleton
`DbContext` registrations, so the lifetime can be reviewed.

## The Problem
`DbContext` is NOT thread-safe and is designed to be short-lived (Scoped). Storing it on a long-lived service is risky and can lead to:
1.  **Threading Crashes**: Two concurrent requests try to use the context at the same time, causing an `InvalidOperationException`.
2.  **Memory Leaks**: The Change Tracker keeps every entity ever fetched in memory forever.
3.  **Data Corruption**: Stale data from previous requests persists in the context.

### Example Violation
```csharp
// Review needed: hosted services are long-lived by default
public sealed class Worker : BackgroundService
{
    private readonly AppDbContext _db;
    public Worker(AppDbContext db) => _db = db;
}
```

### The Fix
Review the service lifetime first. If the type is long-lived, inject `IDbContextFactory<T>` instead or move the `DbContext`
usage to a scoped component. LC030 intentionally has no code fix because the right correction depends on the service
ownership model and registration.

```csharp
// Correct
public class MyService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    public MyService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public void DoWork()
    {
        using var db = _factory.CreateDbContext();
        // ...
    }
}
```

## Analyzer Logic

### ID: `LC030`
### Category: `Architecture`
### Severity: `Info`

### Algorithm
1.  **Targets**:
    -   Instance fields and properties whose type derives from `Microsoft.EntityFrameworkCore.DbContext`.
    -   Constructor parameters whose type derives from `DbContext`, when the type has no stored `DbContext` member to report.
    -   DI calls that register a `DbContext` itself as singleton.
2.  **Strict long-lived proof**:
    -   Report when the containing type implements `IHostedService`, inherits `BackgroundService`, has a conventional ASP.NET Core middleware `Invoke`/`InvokeAsync(HttpContext, ...)` shape, is registered with `AddHostedService<T>()`, or is registered with `AddSingleton(...)`.
    -   Report direct `AddSingleton<TDbContext>()` and `AddDbContext<TContext>(..., ServiceLifetime.Singleton)` registrations.
    -   Stay quiet for controllers, page models, view components, `IMiddleware`, `AddScoped`, `AddTransient`, factories, options, and generic service classes with no long-lived proof.
3.  **Optional project configuration**:
    -   `dotnet_code_quality.LC030.detection_mode = expanded` enables conservative name-based review hints such as `*Singleton*`, `*HostedService`, and `*BackgroundWorker`.
    -   `dotnet_code_quality.LC030.long_lived_types = MyApp.IAlwaysSingleton;MyApp.LongLivedBase` treats matching base types or interfaces as long-lived.

## Test Cases

### Violations
```csharp
public sealed class Worker : BackgroundService { private readonly AppDbContext _db; ... }
public sealed class AuditMiddleware { private readonly AppDbContext _db; public Task InvokeAsync(HttpContext ctx) => Task.CompletedTask; }
services.AddSingleton<AppDbContext>(); // DbContext registered as singleton
services.AddDbContext<AppDbContext>(contextLifetime: ServiceLifetime.Singleton);
```

### Valid
```csharp
public class MyController { public MyController(AppDbContext db) { ... } } // Controllers are scoped
public sealed class ScopedAuditMiddleware : IMiddleware { public ScopedAuditMiddleware(AppDbContext db) { ... } } // IMiddleware can be scoped
services.AddScoped<MyService>();
private readonly IDbContextFactory<AppDbContext> _factory;
```
