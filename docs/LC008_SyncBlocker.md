# Spec: LC008 - Sync-over-Async Blocker

## Goal
Detect usage of synchronous materialization methods (like `ToList`) within an `async` method.

## The Problem
Synchronous calls like `db.Users.ToList()` block the current thread while waiting for the database response. In a web server, this leads to "Thread Starvation," where all available threads are stuck waiting, preventing the server from handling other incoming requests.

LC008 intentionally ignores sync-looking LINQ operators inside `IQueryable` expression trees, such as query-expression subqueries in `let`, `where`, or projection clauses. Those calls are translated as SQL subqueries instead of blocking the async method directly.

### Example Violation
```csharp
public async Task<List<User>> GetUsersAsync()
{
    // Violation: Blocks the thread. Should use ToListAsync().
    return db.Users.ToList();
}
```

### The Fix
Use the asynchronous counterpart and `await` it.

```csharp
public async Task<List<User>> GetUsersAsync()
{
    // Correct: Frees up the thread while waiting
    return await db.Users.ToListAsync();
}
```

## Analyzer Logic

### ID: `LC008`
### Category: `Performance`
### Severity: `Warning`
