# Spec: LC026 - Missing CancellationToken in Async Call

## Goal
Detect usage of EF Core async methods (such as `ToListAsync`, `FirstOrDefaultAsync`, and `SaveChangesAsync`) that are called without a meaningful `CancellationToken` when one is available in scope.

## The Problem
Async database operations can take a long time. In a web application, if a user cancels their request or navigates away, the server should stop processing that request to save resources. If you don't pass a `CancellationToken` to EF Core, the database query will continue to run to completion even if no one is waiting for the result. This wastes database connections, CPU, and memory.

### Example Violation
```csharp
public async Task<List<User>> GetUsers(CancellationToken ct)
{
    // Violation: CancellationToken is available but not passed to EF.
    return await db.Users.ToListAsync();
}
```

### The Fix
Pass the available `CancellationToken` to the async method.

```csharp
public async Task<List<User>> GetUsers(CancellationToken ct)
{
    // Correct: The query will stop if the token is cancelled
    return await db.Users.ToListAsync(ct);
}
```

## Analyzer Logic

### ID: `LC026`
### Category: `Reliability`
### Severity: `Info`

### Algorithm
1.  **Target Methods**: Intercept invocations of methods ending in `Async` that belong to `Microsoft.EntityFrameworkCore`.
2.  **Parameter Check**: Check if the method signature accepts a `CancellationToken`.
3.  **Scope Check**: Only report when a `CancellationToken` local or parameter is available at the call site.
4.  **Argument Check**: Report when the target token parameter is omitted, passed `default`, or passed `CancellationToken.None`.
5.  **Fix Strategy**: Prefer a variable named `cancellationToken`, then `ct`, then the first available token. The fixer appends the token when the optional argument was omitted and replaces an explicit `default`, named `cancellationToken: default`, or `CancellationToken.None` argument when one was already supplied.

## Test Cases

### Violations
```csharp
await db.Users.ToListAsync(); // Missing
await db.Users.ToListAsync(default); // Explicit default token
await db.Users.ToListAsync(CancellationToken.None); // Ignores available token
await db.SaveChangesAsync(); // Missing
```

### Valid
```csharp
await db.Users.ToListAsync(cancellationToken);
await db.SaveChangesAsync(ct);
await db.Users.ToListAsync(); // No token available in scope, so this stays silent
```
