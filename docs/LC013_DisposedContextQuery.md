# Spec: LC013 - Disposed Context Query Leak

## Goal
Detect when an `IQueryable` is returned from a method while the `DbContext` that created it is disposed.

## The Problem
`IQueryable` uses deferred execution. If the `DbContext` is disposed (e.g., at the end of a `using` block), any attempt to run the query later will result in an `ObjectDisposedException`.

### Example Violation
```csharp
public IQueryable<User> GetUsers()
{
    using var db = new AppDbContext();
    // Violation: Caller cannot use this query because db is about to be disposed
    return db.Users.Where(u => u.Active);
}
```

### The Fix
Materialize the query before returning, or ensure the context lifetime is managed correctly (e.g., by the DI container).

```csharp
public List<User> GetUsers()
{
    using var db = new AppDbContext();
    // Correct: Data is fetched before context is disposed
    return db.Users.Where(u => u.Active).ToList();
}
```

## Analyzer Logic

### ID: `LC013`
### Category: `Reliability`
### Severity: `Warning`
