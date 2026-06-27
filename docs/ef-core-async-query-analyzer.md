---
layout: default
title: EF Core Async Query Analyzer
description: Use LinqContraband as an EF Core async query analyzer for sync-over-async EF Core calls, missing cancellation tokens, SaveChangesAsync loops, and async stream buffering.
permalink: /ef-core-async-query-analyzer/
body_class: page-async-query-analyzer
---

# EF Core Async Query Analyzer

LinqContraband helps teams review async EF Core query paths before they reach production. It catches synchronous
database calls inside async methods, missing cancellation tokens on async EF APIs, `SaveChangesAsync` inside loops, and
async stream buffering that should stay streaming.

For the focused missing-token workflow, use the
[EF Core CancellationToken analyzer guide](/LinqContraband/ef-core-cancellation-token-analyzer/).

Install the official NuGet package:

```bash
dotnet add package LinqContraband
```

## Why Async EF Core Calls Need Review

Async code can still block request threads if it calls synchronous EF Core APIs. It can also ignore request cancellation
or buffer an async stream into memory before looping once.

```csharp
public async Task<List<User>> ActiveUsers(CancellationToken cancellationToken)
{
    return db.Users
        .Where(user => user.IsActive)
        .ToList(); // LC008: sync-over-async
}
```

Prefer the async EF Core API and pass the token that represents the current operation:

```csharp
public async Task<List<User>> ActiveUsers(CancellationToken cancellationToken)
{
    return await db.Users
        .Where(user => user.IsActive)
        .ToListAsync(cancellationToken);
}
```

## Rules Covered By The Async Guide

| Rule | What it catches | Why it matters |
| --- | --- | --- |
| [LC008: sync-over-async](/LinqContraband/LC008_SyncBlocker.html) | Synchronous EF Core query, save, find, and bulk APIs inside async contexts. | Blocking database I/O ties up request or worker threads that could be serving other work. |
| [LC026: missing CancellationToken](/LinqContraband/LC026_MissingCancellationToken.html) | Async EF Core calls that omit a token, pass `default`, or pass `CancellationToken.None` while a usable token is in scope. | Cancelled requests and stopping workers should not leave database queries running unnecessarily. See the [EF Core CancellationToken analyzer guide](/LinqContraband/ef-core-cancellation-token-analyzer/) for the focused rollout path. |
| [LC043: async stream buffering](/LinqContraband/LC043_AsyncEnumerableBuffering.html) | Immediate `ToListAsync` or `ToArrayAsync` buffering of an `IAsyncEnumerable<T>` before a single loop. | Buffering loses the memory and latency benefits of streaming. |
| [LC010: SaveChanges inside loop](/LinqContraband/LC010_SaveChangesInLoop.html) | `SaveChanges` or `SaveChangesAsync` inside `for`, `foreach`, `while`, and related loop shapes. | Per-item commits create repeated transactions and partial-progress states. |

## Common Async EF Core Problems

### Sync query inside async method

```csharp
public async Task<int> CountUsers()
{
    return db.Users.Count(); // LC008
}
```

```csharp
public async Task<int> CountUsers(CancellationToken cancellationToken)
{
    return await db.Users.CountAsync(cancellationToken);
}
```

### Async query without cancellation

```csharp
public async Task<User?> FindUser(int id, CancellationToken cancellationToken)
{
    return await db.Users.FirstOrDefaultAsync(user => user.Id == id); // LC026
}
```

```csharp
public async Task<User?> FindUser(int id, CancellationToken cancellationToken)
{
    return await db.Users.FirstOrDefaultAsync(user => user.Id == id, cancellationToken);
}
```

### Async stream buffering before one loop

```csharp
var users = await stream.ToListAsync(); // LC043
foreach (var user in users)
{
    Send(user);
}
```

```csharp
await foreach (var user in stream)
{
    Send(user);
}
```

### SaveChangesAsync inside a loop

```csharp
foreach (var order in orders)
{
    order.Status = OrderStatus.Sent;
    await db.SaveChangesAsync(cancellationToken); // LC010
}
```

```csharp
foreach (var order in orders)
{
    order.Status = OrderStatus.Sent;
}

await db.SaveChangesAsync(cancellationToken);
```

## Safer Async Patterns

- Use EF Core's async counterpart inside async code: `ToListAsync`, `CountAsync`, `FirstOrDefaultAsync`,
  `SaveChangesAsync`, `FindAsync`, `ExecuteUpdateAsync`, and `ExecuteDeleteAsync`.
- Pass the available `CancellationToken` through async query and save APIs.
- Do not wrap synchronous EF Core database calls in `Task.Run` to make them look async.
- Use `await foreach` when an `IAsyncEnumerable<T>` only needs one pass.
- Batch entity changes and call `SaveChangesAsync` once unless each item has a deliberate commit boundary.

## Review Checklist

- Does every async request, worker, or handler path avoid synchronous EF Core database APIs?
- Do `ToListAsync`, `CountAsync`, `FirstOrDefaultAsync`, `SaveChangesAsync`, and similar calls receive the available token?
- Are async streams processed with `await foreach` instead of buffered into memory for one loop?
- Are write loops batching changes before a single `SaveChangesAsync` call?
- Are deliberate exceptions documented with narrow suppressions instead of broad analyzer disablement?

## CI Severity Starter

```ini
[*.cs]

# Async EF Core execution
dotnet_diagnostic.LC008.severity = warning
dotnet_diagnostic.LC026.severity = suggestion
dotnet_diagnostic.LC043.severity = suggestion
dotnet_diagnostic.LC010.severity = warning
```

Use the [EF Core query analyzer CI guide](/LinqContraband/ef-core-query-analyzer-ci/) to promote selected async rules
from local warnings to pull-request checks.

## Related Guides

- [EF Core query performance checklist](/LinqContraband/ef-core-query-performance-checklist/)
- [EF Core analyzer rules](/LinqContraband/ef-core-analyzer-rules/)
- [EF Core CancellationToken analyzer](/LinqContraband/ef-core-cancellation-token-analyzer/)
- [EF Core SaveChanges in loop analyzer](/LinqContraband/ef-core-savechanges-in-loop-analyzer/)
- [EF Core DbContext lifetime analyzer](/LinqContraband/ef-core-dbcontext-lifetime-analyzer/)
- [EF Core query analyzer for CI](/LinqContraband/ef-core-query-analyzer-ci/)

## Official Links

- Canonical repository: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Official NuGet package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Documentation hub: [georgepwall1991.github.io/LinqContraband](https://georgepwall1991.github.io/LinqContraband/)
