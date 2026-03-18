# Spec: LC013 - Disposed Context Query Leak

## Goal
Detect when an `IQueryable` or `IAsyncEnumerable` is returned from a method while the `DbContext` that created it is disposed.

## The Problem
`IQueryable` and `IAsyncEnumerable` use deferred execution. If the `DbContext` is disposed (e.g., at the end of a `using` block), any attempt to run the query later will result in an `ObjectDisposedException`.

### Example Violation
```csharp
public IQueryable<User> GetUsers()
{
    using var db = new AppDbContext();
    var query = db.Users.Where(u => u.Active);

    // Violation: Caller cannot use this query because db is about to be disposed
    return query;
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

- Tracks the returned query back through single-assignment local aliases in the same executable root.
- Handles conditional, coalesce, and switch-expression returns branch by branch.
- Only reports when the origin is a disposed local `DbContext`.
- Ignores nested local-function and lambda returns to avoid false positives when the outer method materializes the query before exiting.
- No automatic code fix is offered for LC013 in this pass.

### ID: `LC013`
### Category: `Reliability`
### Severity: `Warning`
