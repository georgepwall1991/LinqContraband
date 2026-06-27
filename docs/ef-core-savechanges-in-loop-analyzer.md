---
layout: default
title: EF Core SaveChanges in Loop Analyzer
description: Use LinqContraband as an EF Core SaveChanges in loop analyzer for repeated SaveChanges calls, N+1 writes, sync saves in async methods, and batch-write review.
permalink: /ef-core-savechanges-in-loop-analyzer/
body_class: page-savechanges-loop-analyzer
---

# EF Core SaveChanges in Loop Analyzer

LinqContraband is an EF Core `SaveChanges` in loop analyzer for .NET projects that want repeated database writes to
show up during development and CI. It helps reviewers catch per-item commits, sync saves in async methods, and bulk
update loops before they turn one request into hundreds of database roundtrips.

Install the official analyzer package:

```bash
dotnet add package LinqContraband
```

## Why SaveChanges Inside a Loop Hurts

`SaveChanges` and `SaveChangesAsync` are unit-of-work boundaries. Calling them once after a batch lets EF Core commit the
tracked changes together. Calling them inside a loop opens the door to one database write per item:

```csharp
foreach (var user in users)
{
    user.LastLogin = clock.UtcNow;
    await db.SaveChangesAsync();
}
```

That shape is easy to miss in review because each save looks correct in isolation. At production row counts, it can mean
many transactions, many roundtrips, slower request latency, and harder-to-reason-about partial progress.

The batched shape is usually clearer:

```csharp
foreach (var user in users)
{
    user.LastLogin = clock.UtcNow;
}

await db.SaveChangesAsync();
```

## LinqContraband Rules That Help

| Rule | What it detects | Review direction |
| --- | --- | --- |
| [LC010: SaveChanges inside loop](/LinqContraband/LC010_SaveChangesInLoop.html) | Direct `SaveChanges` or `SaveChangesAsync` calls inside `for`, `foreach`, `await foreach`, `while`, or `do` loops. | Move the save outside the loop when one batched commit preserves the business rule. |
| [LC032: use ExecuteUpdate for bulk updates](/LinqContraband/LC032_ExecuteUpdateForBulkUpdates.html) | A provable tracked update loop followed by `SaveChanges` where a uniform scalar change can become `ExecuteUpdate`. | Consider a set-based update when bypassing tracking, callbacks, interceptors, and deferred-save timing is acceptable. |
| [LC012: use ExecuteDelete instead of RemoveRange](/LinqContraband/LC012_OptimizeRemoveRange.html) | Query-shaped `RemoveRange` calls that can avoid loading rows before delete. | Use `ExecuteDelete` only after checking cascade, interceptor, and unit-of-work timing requirements. |
| [LC008: sync-over-async blocker](/LinqContraband/LC008_SyncBlocker.html) | Synchronous `SaveChanges` inside async control flow when `SaveChangesAsync` is available. | Use async EF Core APIs in async methods so request threads are not blocked. |

## Safer Write Patterns

Batch ordinary tracked changes and commit once:

```csharp
foreach (var user in users)
{
    user.LastLogin = clock.UtcNow;
}

await db.SaveChangesAsync(cancellationToken);
```

Use `ExecuteUpdateAsync` for a uniform scalar change that does not need entity callbacks or tracked state:

```csharp
await db.Users
    .Where(user => user.IsActive)
    .ExecuteUpdateAsync(setters => setters
        .SetProperty(user => user.LastLogin, user => clock.UtcNow),
        cancellationToken);
```

Keep per-item saves only when the boundary is deliberate:

```csharp
foreach (var message in outboxMessages)
{
    await PublishAsync(message, cancellationToken);
    message.MarkPublished(clock.UtcNow);

    // Deliberate durability boundary: each message records progress independently.
    await db.SaveChangesAsync(cancellationToken);
}
```

In that case, document the reason with a suppression or code comment so the exception survives future review.

## Review Checklist

1. Does the loop need one transaction per item, or can the changes be committed once after the loop?
2. Would batching change retry behaviour, progress durability, audit timing, or external side effects?
3. Is a uniform scalar update better expressed with `ExecuteUpdate`?
4. Is a query-shaped delete better expressed with `ExecuteDelete`?
5. In async methods, is the save using `SaveChangesAsync` and passing the available cancellation token?

## CI Severity Starter

Start with repeated saves as warnings, then promote them once the team has cleared intentional per-item commits:

```ini
[*.cs]

# Repeated writes and async save policy
dotnet_diagnostic.LC010.severity = warning
dotnet_diagnostic.LC008.severity = warning

# Set-based write opportunities
dotnet_diagnostic.LC032.severity = suggestion
dotnet_diagnostic.LC012.severity = suggestion
```

Use the [EF Core query analyzer CI guide](/LinqContraband/ef-core-query-analyzer-ci/) when these diagnostics should run
on every pull request. Use the [EF Core ExecuteUpdate analyzer guide](/LinqContraband/ef-core-executeupdate-analyzer/)
when the loop can become a set-based update or delete.

## Official Links

- Canonical repository: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Official NuGet package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Full rule catalog: [georgepwall1991.github.io/LinqContraband/rule-catalog.html](https://georgepwall1991.github.io/LinqContraband/rule-catalog.html)
- Safe install guidance: [Official LinqContraband downloads and authenticity](/LinqContraband/security-and-authenticity/)
