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

- A task already awaited, or proven complete, before the blocking access: `await task;`, `await Task.WhenAll(t1, t2);`, `task.Wait();` (which reports itself), or a check such as `task.IsCompletedSuccessfully ? task.Result : ...`. Any other use of the local before the access, and any `await` between the assignment and the access, count as possibly completing it; a zero-timeout poll such as `task.Wait(0)` does not, so a later `task.Result` still reports, except in the true branch of an `if` or `?:` whose condition is that poll: `if (task.Wait(0)) users = task.Result;` stays quiet, as does `if (task.IsCompleted) users = task.Result;`.
- Tasks that do not come straight from EF Core: `Task.FromResult`, the application's own `...Async` methods, `Task.Run(() => db.Users.ToListAsync()).Result`, fields and properties that hold a task, and locals assigned more than once or copied from another local.
- Queries over an in-memory collection wrapped with `AsQueryable()`: the EF Core async operators throw on those instead (LC060's case).
- A task stored in a local outside the lambda that blocks on it.
- `task.Wait(0)`, `task.Wait(TimeSpan.Zero)` and other zero timeouts (`default`, `default(TimeSpan)`, `new TimeSpan()`, `new TimeSpan(0)`, a constant 0), which only check whether the task has finished. Any other timeout blocks and is reported.

## Code Fix

- In an `async` method, lambda or local function, the blocking access becomes `await` on the task: `db.Users.ToListAsync().Result` becomes `await db.Users.ToListAsync()`, `db.SaveChangesAsync().Wait()` becomes `await db.SaveChangesAsync()`, and `task.Result` becomes `await task`. A member access on the result gets parentheses: `(await db.Users.ToListAsync()).Count`.
- In code that is not async, a direct call becomes its synchronous counterpart: `ToListAsync().Result` becomes `ToList()`, `AddRangeAsync(...).Wait()` becomes `AddRange(...)`. A `CancellationToken` argument is dropped, because the synchronous method takes none, and `using System.Linq;` is added when `ToList` and the other `Enumerable` terminals need it.

No fix is offered where neither rewrite is provably safe:

- No synchronous fix for `SaveChangesAsync`, any overload: a `SaveChangesInterceptor` that implements only `SavingChangesAsync`, or a `SaveChangesAsync` override (auditing, soft delete), runs only on the async path, so `SaveChanges()` would skip it, and the fixer cannot see which interceptors are registered. In async code it is still awaited.

- A non-async lambda inside an async method, or a `lock` body: `await` is not allowed there, and the synchronous call would be LC008's finding.
- A task stored in a local outside async code.
- No `await` fix when a `ref struct` value, such as a `Span<T>` local or parameter, or a `ref` or `ref readonly` local, is used after the blocking access, or anywhere inside a `ref struct`, where `this` can be read implicitly: such a value cannot live across an `await`.
- No synchronous fix when the `CancellationToken` argument could have an effect, such as `ToListAsync(GetToken())` or `ToListAsync(source.Token)`: dropping it would drop that call or property read (`CancellationTokenSource.Token` throws once the source is disposed). Only locals, parameters, fields (static, or on `this`, a local or a parameter), `default`, constants and `CancellationToken.None` are dropped.
- `Wait(timeout)`, which returns whether the task finished.
- Code inside a `try`, in the same member, with a catch that can see the `AggregateException` that `.Result` and `.Wait()` throw and the rewrites do not: a bare `catch`, `catch (Exception)`, `catch (SystemException)`, or a catch of `AggregateException` or one of its base types, with or without a `when` filter. A catch that cannot be an `AggregateException`, such as `catch (InvalidOperationException)` or `catch (DbUpdateException)`, keeps the fix.
- An operation with no synchronous counterpart, such as `ForEachAsync`, and the static form `EntityFrameworkQueryableExtensions.ToListAsync(query)`.
- No synchronous fix unless the receiver's type is closed to other assemblies: `sealed`, or declared (or nested) `internal` or `private` in a project without `InternalsVisibleTo`. A public unsealed type, including `DbContext`, `DbSet<T>` and `DatabaseFacade` themselves, may hold an object from another assembly that overrides only the async method, and the synchronous call would skip that override. So `FindAsync`, `AddAsync` and the `Database` methods get only the `await` fix.
- No synchronous fix when the application overrides the async method but not the synchronous one, as a context that overrides `AddRangeAsync` and inherits `AddRange` does: `AddRange()` would skip the override. This applies to the receiver's own type and to any type in the project derived from it, since a `DbContext` variable can hold such a context. Overloads are matched by signature: every overridden async overload needs an override of the synchronous overload with the same parameters minus the token, at the same level or deeper, so an override of `FindAsync(object[], CancellationToken)` needs an override of `Find(object[])`. The rewritten call must also bind to that matching overload. The walk runs through every application type, whatever its namespace, and stops at EF Core's own types, found by their assembly. The `await` fix is unaffected.

A preprocessor directive (`#pragma`, `#if`, `#nullable`, ...) inside the blocking expression withholds both fixes. Comments between the task and the blocking access, as in `db.Users.ToListAsync() /* why */ .Result`, move after the new expression; a `//` comment keeps a line break after it.

The fixer compiles the rewritten document and offers nothing when the rewrite would add an error or change the type of the value. The synchronous call must bind to `System.Linq.Enumerable`, `System.Linq.Queryable` or a type from an EF Core assembly (an application context's override counts as the `DbContext` method it overrides), so a project's own helper in a `Microsoft.EntityFrameworkCore.*` namespace is never the target.

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
var users = new List<User>().AsQueryable().ToListAsync().Result;
var n = Task.FromResult(1).Result;
```
