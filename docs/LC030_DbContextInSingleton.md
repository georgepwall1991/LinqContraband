# Spec: LC030 - Review DbContext Lifetime Mismatches

## Goal
Flag likely long-lived types that store a `DbContext` in a field or property so the lifetime can be reviewed.

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
usage to a scoped component.

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
1.  **Target Classes**: Inspect classes that are NOT `DbContext` themselves.
2.  **Lifetime Heuristic**:
    -   This rule is intentionally heuristic. It does **not** prove singleton registration.
    -   Prefer symbol-based long-lived signals such as `IHostedService`, `BackgroundService`, or conventional middleware signatures.
    -   Skip obvious scoped/per-request types such as controllers, page models, view components, and `IMiddleware` implementations.
    -   Treat the result as a review hint, not an automatic architecture rewrite.

## Test Cases

### Violations
```csharp
public sealed class Worker : BackgroundService { private readonly AppDbContext _db; ... }
public sealed class AuditMiddleware { private readonly AppDbContext _db; public Task InvokeAsync(HttpContext ctx) => Task.CompletedTask; }
```

### Valid
```csharp
public class MyController { public MyController(AppDbContext db) { ... } } // Controllers are scoped
public sealed class ScopedAuditMiddleware : IMiddleware { public ScopedAuditMiddleware(AppDbContext db) { ... } } // IMiddleware can be scoped
```

## Implementation Plan
1.  Create `LC030_DbContextInSingleton` directory.
2.  Implement `DbContextInSingletonAnalyzer`.
3.  Implement tests.
