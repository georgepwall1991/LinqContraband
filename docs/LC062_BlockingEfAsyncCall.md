---
layout: default
title: "LC062: Blocking on an EF Core async call"
description: "LC062 flags .Result, .Wait() and .GetAwaiter().GetResult() on EF Core async calls, which starve the thread pool and deadlock under a SynchronizationContext."
---

# LC062: Blocking on an EF Core async call

## In Plain Terms

You phone the kitchen, then hold the line and refuse to let anyone else use the phone until the food is ready. The kitchen needs that phone to tell you the food is ready.

## Goal

Detect code that blocks on the task of an EF Core async operation with `.Result`, `.Wait()` or `.GetAwaiter().GetResult()`.

## The Problem

`ToListAsync()`, `SaveChangesAsync()` and the other EF Core async methods return a task that finishes on a continuation. Blocking on that task (sync-over-async) holds the calling thread while the database works, and the continuation needs a thread of its own to finish the task:

- Under load, every blocked request holds a thread-pool thread and needs a second one to finish, so the pool starves and requests time out. See [asynchronous programming](https://learn.microsoft.com/ef/core/performance/efficient-querying#asynchronous-programming) in the EF Core performance guide.
- Under a `SynchronizationContext` (WinForms, WPF, classic ASP.NET), the continuation is posted back to the thread that is blocked waiting for it, and the call deadlocks. See [EF Core async programming](https://learn.microsoft.com/ef/core/miscellaneous/async).

```csharp
// Violations: each one blocks the thread until EF Core finishes.
var users = db.Users.ToListAsync().Result;
db.SaveChangesAsync().Wait();
var user = db.Users.FirstOrDefaultAsync(u => u.Id == id).GetAwaiter().GetResult();
```

## The Fix

Await the task. If the calling code cannot be async, call the synchronous EF Core method instead, which does the same work without a second thread:

```csharp
// In async code
var users = await db.Users.ToListAsync(cancellationToken);
await db.SaveChangesAsync(cancellationToken);

// In code that cannot be async
var users = db.Users.ToList();
db.SaveChanges();
```

LC008 is the reverse case: a synchronous EF Core call such as `ToList()` inside an async method.

## Analyzer Logic

### ID: `LC062`
### Category: `Performance`
### Severity: `Warning`

Reports `.Result` on `Task<T>` or `ValueTask<T>`, any `Task.Wait(...)` overload, and `.GetAwaiter().GetResult()` (also after `ConfigureAwait(...)`), in any method, when the task comes from one of these EF Core async operations directly, through `ConfigureAwait(...)` or `ValueTask.AsTask()`, or through a local assigned once from it:

1. A query operator in `EntityFrameworkQueryableExtensions` or `RelationalQueryableExtensions` (`ToListAsync`, `FirstOrDefaultAsync`, `CountAsync`, `ForEachAsync`, `ExecuteDeleteAsync`, `ExecuteUpdateAsync`, ...).
2. `SaveChangesAsync`, including an override in the application's context, and `FindAsync`, `AddAsync` and `AddRangeAsync` on a `DbContext` or `DbSet<T>`. `FindAsync` and `AddAsync` return a `ValueTask`.
3. The async methods of `DatabaseFacade` (`BeginTransactionAsync`, `EnsureCreatedAsync`, ...) and `RelationalDatabaseFacadeExtensions` (`MigrateAsync`, `ExecuteSqlRawAsync`, `ExecuteSqlAsync`, `ExecuteSqlInterpolatedAsync`, ...).

## When it stays quiet (non-goals)

- A task already awaited, or proven complete, before the blocking access: `await task;`, `await Task.WhenAll(t1, t2);`, `task.Wait();` (which reports itself), or a check such as `task.IsCompletedSuccessfully ? task.Result : ...`. Any other use of the local before the access, and any `await`, `await foreach` or `await using` (statement or declaration) between the assignment and the access, count as possibly completing it; a zero-timeout poll such as `task.Wait(0)` does not, so a later `task.Result` still reports, except in the true branch of an `if` or `?:` whose condition is that poll, also written as `task.Wait(0) == true`, `!= false`, `is true` or one side of `&&` (and in the right operand of that `&&`): `if (task.Wait(0)) users = task.Result;` stays quiet, as do `if (ready && task.Wait(0)) users = task.Result;` and `if (task.IsCompleted) users = task.Result;`. A `== false`, `!= true` or `||` condition does not guard the access. `nameof(task.Result)` stays quiet. `Wait(CancellationToken)` has no timeout and still reports.
- Tasks that do not come straight from EF Core: `Task.FromResult`, the application's own `...Async` methods, `Task.Run(() => db.Users.ToListAsync()).Result`, fields and properties that hold a task, and locals assigned more than once or copied from another local.
- Queries over an in-memory collection wrapped with `AsQueryable()`: the EF Core async operators throw on those instead (LC060's case).
- A task stored in a local outside the lambda that blocks on it.
- `task.Wait(0)`, `task.Wait(TimeSpan.Zero)` and other zero timeouts (`default`, `default(TimeSpan)`, `new TimeSpan()`, `new TimeSpan(0)`, `TimeSpan.FromMilliseconds(0)` and the other `TimeSpan.FromXxx(0)` factories, a constant 0), which only check whether the task has finished. Any other timeout blocks and is reported.

## Code Fix

In an `async` method, lambda or local function, the blocking access becomes `await` on the task: `db.Users.ToListAsync().Result` becomes `await db.Users.ToListAsync()`, `db.SaveChangesAsync().Wait()` becomes `await db.SaveChangesAsync()`, and `task.Result` becomes `await task`. A member access on the result gets parentheses: `(await db.Users.ToListAsync()).Count`. Comments between the task and the blocking access, as in `db.Users.ToListAsync() /* why */ .Result`, move after the new expression; a `//` comment keeps a line break after it.

There is no synchronous fix (`ToListAsync().Result` to `ToList()`), because it cannot be proven to do the same thing: a `SaveChangesInterceptor` or `DbCommandInterceptor` that implements only the async callbacks, an application override of only the async method (auditing, soft delete), and the evaluation of arguments the synchronous method does not take all run only on the async path, and the fixer cannot see which interceptors are registered. In code that cannot be async, call the synchronous method yourself once you have checked those.

No fix is offered:

- Outside async code, including a non-async lambda inside an async method, and inside a `lock` body, where `await` is not allowed.
- Inside the `try` block of a `try` statement, in the same member, that has any catch clause, typed, untyped or filtered: `.Result` and `.Wait()` throw `AggregateException` and `await` does not, so the rewrite can change which handler runs. A `try` with only a `finally` keeps the fix.
- When a `ref struct` value, such as a `Span<T>` local, or a `ref` or `ref readonly` local, is used after the blocking access, or anywhere inside a `ref struct`, where `this` can be read implicitly: such a value cannot live across an `await`.
- For `Wait(timeout)`, which returns whether the task finished, and for `Wait(CancellationToken)`, another argument-taking overload the fixer does not rewrite.
- When a preprocessor directive (`#pragma`, `#if`, `#nullable`, ...) sits inside the blocking expression.

The fixer compiles the rewritten document and offers nothing when the rewrite would add an error or change the type of the value.

## Test Cases

### Violations

```csharp
var users = db.Users.ToListAsync().Result;
db.SaveChangesAsync().Wait();
var user = db.Users.FindAsync(id).Result;
var task = db.Users.CountAsync(); var count = task.Result;
```

### Valid

```csharp
var users = await db.Users.ToListAsync(ct);
var task = db.Users.ToListAsync(); await task; var users = task.Result;
var task = db.Users.ToListAsync(); await foreach (var n in Empty()) { } var users = task.Result;
var name = nameof(task.Result);
var users = new List<User>().AsQueryable().ToListAsync().Result;
var n = Task.FromResult(1).Result;
```
