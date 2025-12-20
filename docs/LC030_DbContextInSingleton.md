# Spec: LC030 - Avoid DbContext in Singleton Services

## Goal
Detect usage of `DbContext` (or derived types) when injected into a service that is likely to be a `Singleton`.

## The Problem
`DbContext` is NOT thread-safe and is designed to be short-lived (Scoped). If you inject it into a `Singleton` service, that single `DbContext` instance will be shared across all users and all threads for the lifetime of the application. This leads to:
1.  **Threading Crashes**: Two concurrent requests try to use the context at the same time, causing an `InvalidOperationException`.
2.  **Memory Leaks**: The Change Tracker keeps every entity ever fetched in memory forever.
3.  **Data Corruption**: Stale data from previous requests persists in the context.

### Example Violation
```csharp
// Violation: Singleton service holding a DbContext
public class MySingletonService
{
    private readonly AppDbContext _db;
    public MySingletonService(AppDbContext db) => _db = db;
}
```

### The Fix
Inject `IDbContextFactory<T>` instead and create a short-lived context when needed, or make the service `Scoped`.

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
### Severity: `Warning`

### Algorithm
1.  **Target Classes**: Inspect classes that are NOT `DbContext` themselves.
2.  **Lifetime Heuristic**: 
    -   Check for classes with `[Singleton]` attributes (from common libraries like `Microsoft.Extensions.DependencyInjection` if applicable, though usually registration is in `Startup.cs`).
    -   *Robust approach*: Since registration is usually elsewhere, we can look for specific naming conventions (e.g., `...Singleton...`) or focus on fields/properties of type `DbContext` in classes that don't look like they are scoped.
    -   *Even Better*: Warn on ANY field/property of type `DbContext` in a class that is NOT a `Controller`, `Middleware`, or `DbContext` unless it's properly disposed? 
    -   *Decision for MVP*: Focus on detecting `DbContext` in fields of classes that are likely to be singletons, or simply warn about `DbContext` as a long-lived field in *any* class, as it's a risky pattern.

## Test Cases

### Violations
```csharp
public class MyManager { private readonly AppDbContext _db; ... }
```

### Valid
```csharp
public class MyController { public MyController(AppDbContext db) { ... } } // Controllers are scoped
```

## Implementation Plan
1.  Create `LC030_DbContextInSingleton` directory.
2.  Implement `DbContextInSingletonAnalyzer`.
3.  Implement tests.
