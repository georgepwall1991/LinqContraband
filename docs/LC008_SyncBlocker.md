---
layout: default
title: "Spec: LC008 - Sync-over-Async Blocker"
---

# Spec: LC008 - Sync-over-Async Blocker

## Goal
Detect synchronous EF Core query and save operations inside an `async` context when an async counterpart exists.

## The Problem
Synchronous calls like `db.Users.ToList()` block the current thread while waiting for the database response. In a web server, this leads to "Thread Starvation," where all available threads are stuck waiting, preventing the server from handling other incoming requests.

LC008 focuses on database I/O that happens directly inside async control flow. It reports mapped synchronous methods when they execute against EF-backed sources:

- query materializers: `ToList`, `ToArray`, `ToDictionary`, and `ToHashSet`
- query terminals: `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault`, `Count`, `LongCount`, `Any`, `All`, `Min`, `Max`, `Sum`, and `Average`
- context/set operations: `SaveChanges`, `Find`, `ExecuteUpdate`, and `ExecuteDelete`

### Example Violation
```csharp
public async Task<List<User>> GetUsersAsync()
{
    // Violation: Blocks the thread. Should use ToListAsync().
    return db.Users.ToList();
}
```

```csharp
public async Task<int> CountUsersAsync()
{
    // Violation: Count() blocks the async method.
    return db.Users.Count();
}
```

```csharp
public async Task SaveAsync()
{
    UpdateEntities();

    // Violation: synchronous database save inside async flow.
    db.SaveChanges();
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

```csharp
public async Task<int> CountUsersAsync()
{
    return await db.Users.CountAsync();
}
```

```csharp
public async Task SaveAsync()
{
    UpdateEntities();
    await db.SaveChangesAsync();
}
```

## Async Counterpart Families
LC008 uses a fixed sync-to-async map. If a method has no mapped async counterpart, LC008 does not guess at one and does not report it as a sync-over-async violation.

| Sync call | Suggested async call |
| --- | --- |
| `ToList`, `ToArray`, `ToDictionary`, `ToHashSet` | `ToListAsync`, `ToArrayAsync`, `ToDictionaryAsync`, `ToHashSetAsync` |
| `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault` | matching `*Async` terminal |
| `Count`, `LongCount`, `Any`, `All`, `Min`, `Max`, `Sum`, `Average` | matching `*Async` aggregate |
| `SaveChanges` | `SaveChangesAsync` |
| `Find` | `FindAsync` |
| `ExecuteUpdate`, `ExecuteDelete` | `ExecuteUpdateAsync`, `ExecuteDeleteAsync` |

When an EF API truly has no async equivalent, the rule's advice is not to wrap it in `Task.Run`. Keep that work outside the hot async request path, move the surrounding workflow to a synchronous boundary, or document the deliberate blocking call with a suppression.

## Async Context Boundaries
LC008 reports inside:

- `async Task`, `async ValueTask`, and other async methods
- async lambdas
- non-async lambdas and capturing local functions nested inside an async method

LC008 intentionally ignores sync-looking LINQ operators inside `IQueryable` expression trees, such as query-expression subqueries in `let`, `where`, or projection clauses. Those calls are translated as SQL subqueries instead of blocking the async method directly.

```csharp
public async Task QueryAsync()
{
    var query =
        from user in db.Users
        let matches = db.Users.Where(inner => inner.Id == user.Id).ToList()
        select new { user, matches };

    await Task.Delay(1);
}
```

The async-context walk treats a **`static` local function as a hard synchronous boundary**: it cannot capture the enclosing async context, `await` is illegal inside it, and a diagnostic there would be unfixable, so a sync EF call inside a `static` local function declared in an `async` method stays quiet. A *capturing* non-static local function or lambda inside an async method still flags because it is refactoring debt the rule deliberately surfaces, and an `async static` local function flags too because it can await the async counterpart.

```csharp
public async Task LoadAsync()
{
    var users = Load(db);
    await Task.Delay(1);

    static List<User> Load(AppDbContext db)
    {
        return db.Users.ToList(); // no LC008: static local synchronous boundary
    }
}
```

## Fixer Behavior
The fixer is intentionally narrow. It replaces the method name with the mapped async name and wraps the invocation in `await` only when `await` is legal at that syntax location.

- `db.Users.ToList()` becomes `await db.Users.ToListAsync()`.
- `db.SaveChanges()` becomes `await db.SaveChangesAsync()`.
- When the sync call feeds a following member, element, invocation, or null-conditional access, the fixer parenthesizes the awaited result: `db.Users.ToList().Count` becomes `(await db.Users.ToListAsync()).Count`, and `db.Users.FirstOrDefault()?.Name` becomes `(await db.Users.FirstOrDefaultAsync())?.Name`.
- Query-expression subqueries that are part of the provider expression do not report and therefore do not offer a fix.
- Diagnostics inside non-async lambdas or non-async local functions can report, but the fixer stays quiet because inserting `await` there would not compile without refactoring the delegate/local-function shape.
- The fixer does not add cancellation tokens or choose overloads; pass tokens explicitly after the rewrite when the surrounding code has one.

## Analyzer Logic
- Reports only mapped sync methods on EF Core `DbContext`, `DbSet`, or `IQueryable` sources.
- Does not report plain in-memory `IEnumerable` work or synchronous methods outside async contexts.
- Does not report sync-looking operators that are part of an `IQueryable` expression tree and will be translated by the provider.

### ID: `LC008`
### Category: `Performance`
### Severity: `Warning`
