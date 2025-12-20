# Spec: LC026 - Missing CancellationToken in Async Call

## Goal
Detect usage of EF Core async extension methods (like `ToListAsync`, `FirstOrDefaultAsync`, `SaveChangesAsync`) that are called without a `CancellationToken`.

## The Problem
Async database operations can take a long time. In a web application, if a user cancels their request or navigates away, the server should stop processing that request to save resources. If you don't pass a `CancellationToken` to EF Core, the database query will continue to run to completion even if no one is waiting for the result. This wastes database connections, CPU, and memory.

### Example Violation
```csharp
public async Task<List<User>> GetUsers(CancellationToken ct)
{
    // Violation: CancellationToken is available but not passed to EF
    return await db.Users.ToListAsync(); 
}
```

### The Fix
Pass the available `CancellationToken` to the async method.

```csharp
public async Task<List<User>> GetUsers(CancellationToken ct)
{
    // Correct: The query will stop if the token is cancelled
    return await db.ToListAsync(ct);
}
```

## Analyzer Logic

### ID: `LC026`
### Category: `Reliability`
### Severity: `Warning`

### Algorithm
1.  **Target Methods**: Intercept invocations of methods ending in `Async` that belong to `Microsoft.EntityFrameworkCore`.
2.  **Parameter Check**: Check if the method signature accepts a `CancellationToken`.
3.  **Argument Check**: Check if an argument is actually passed for that parameter.
    -   *Note*: If the argument is `default` or `CancellationToken.None`, we should still warn if there is a `CancellationToken` available in the method scope.

## Test Cases

### Violations
```csharp
await db.Users.ToListAsync(); // Missing
await db.SaveChangesAsync(); // Missing
```

### Valid
```csharp
await db.Users.ToListAsync(cancellationToken);
await db.SaveChangesAsync(ct);
```

## Implementation Plan
1.  Create `LC026_MissingCancellationToken` directory.
2.  Implement `MissingCancellationTokenAnalyzer`.
3.  Implement `MissingCancellationTokenFixer`.
4.  Implement tests.
