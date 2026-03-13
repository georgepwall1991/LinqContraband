# Spec: LC030 - Review DbContext Lifetime Mismatches

## Goal
Flag classes that store a `DbContext` in a field or property so the lifetime can be reviewed.

## The Problem
`DbContext` is NOT thread-safe and is designed to be short-lived (Scoped). Storing it on a long-lived service is risky and can lead to:
1.  **Threading Crashes**: Two concurrent requests try to use the context at the same time, causing an `InvalidOperationException`.
2.  **Memory Leaks**: The Change Tracker keeps every entity ever fetched in memory forever.
3.  **Data Corruption**: Stale data from previous requests persists in the context.

### Example Violation
```csharp
// Review needed: this may be fine for scoped types, risky for long-lived services
public class MyService
{
    private readonly AppDbContext _db;
    public MyService(AppDbContext db) => _db = db;
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
    -   It skips obvious scoped MVC types such as controllers and page models.
    -   Treat the result as a review hint, not an automatic architecture rewrite.

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
