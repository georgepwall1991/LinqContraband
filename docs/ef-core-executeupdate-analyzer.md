---
layout: default
title: EF Core ExecuteUpdate Analyzer
description: Use LinqContraband as an EF Core ExecuteUpdate analyzer for set-based updates, ExecuteDelete replacements, and missing Where filters before bulk writes.
permalink: /ef-core-executeupdate-analyzer/
body_class: page-executeupdate-analyzer
---

# EF Core ExecuteUpdate Analyzer

LinqContraband is an EF Core `ExecuteUpdate` analyzer for .NET projects that want bulk write opportunities and bulk
write safety issues to appear during development and CI. It helps reviewers spot slow tracked update loops, expensive
`RemoveRange` deletes, and unfiltered `ExecuteUpdate` or `ExecuteDelete` calls before they reach production data.

Install the official analyzer package:

```bash
dotnet add package LinqContraband
```

## Why Bulk Writes Need Review

EF Core's set-based APIs can be dramatically faster than loading every row, changing tracked entities one by one, and
calling `SaveChanges`. The trade-off is that `ExecuteUpdate` and `ExecuteDelete` run immediately in the database and
bypass EF Core change tracking.

That makes two review questions important:

- Can this tracked loop or `RemoveRange` call safely become a set-based SQL operation?
- Is every destructive bulk operation filtered before it touches rows?

The slow shape looks like this:

```csharp
foreach (var user in db.Users.Where(user => user.IsInactive))
{
    user.Status = "Archived";
}

db.SaveChanges();
```

The dangerous shape is the opposite problem:

```csharp
db.Users.ExecuteDelete();
```

The first query may waste time and memory. The second can affect the whole table.

## LinqContraband Rules That Help

| Rule | What it detects | Review direction |
| --- | --- | --- |
| [LC032: use ExecuteUpdate for bulk updates](/LinqContraband/LC032_ExecuteUpdateForBulkUpdates.html) | A provable tracked update loop followed by `SaveChanges` where a uniform scalar update can become `ExecuteUpdate`. | Use `ExecuteUpdate` or `ExecuteUpdateAsync` when bypassing tracking, callbacks, interceptors, and deferred-save timing is acceptable. |
| [LC012: use ExecuteDelete instead of RemoveRange](/LinqContraband/LC012_OptimizeRemoveRange.html) | `RemoveRange(query)` where the argument is still query-shaped and no relevant later `SaveChanges` would make the rewrite behaviour-changing. | Use `ExecuteDelete` or `ExecuteDeleteAsync` after confirming cascades, interceptors, and unit-of-work timing are not required. |
| [LC035: missing Where before ExecuteDelete or ExecuteUpdate](/LinqContraband/LC035_MissingWhereBeforeExecuteDeleteUpdate.html) | EF Core bulk execute calls on a query with no proven `Where` filter. | Add the tenant, lifecycle, account, or business filter before the bulk write. |

## Safer Bulk Write Patterns

Use `ExecuteUpdate` for a uniform scalar change:

```csharp
await db.Users
    .Where(user => user.IsInactive)
    .ExecuteUpdateAsync(setters => setters
        .SetProperty(user => user.Status, user => "Archived"));
```

Use `ExecuteDelete` for filtered deletes that do not need tracked entity callbacks:

```csharp
await db.Logs
    .Where(log => log.CreatedAt < cutoff)
    .ExecuteDeleteAsync();
```

Keep the normal tracked workflow when entity callbacks, interceptors, optimistic concurrency handling, or deferred
`SaveChanges` timing are part of the business rule:

```csharp
var users = await db.Users
    .Where(user => user.RequiresDomainEvent)
    .ToListAsync();

foreach (var user in users)
{
    user.ArchiveWithDomainEvent(clock.UtcNow);
}

await db.SaveChangesAsync();
```

## Bulk Write Safety Checklist

1. Is the target set filtered by tenant, account, lifecycle state, date range, or another business boundary?
2. Would bypassing change tracking skip required domain events, entity callbacks, save interceptors, or client-side cascades?
3. Does the code rely on the affected-row count returned from `SaveChanges`?
4. Does the update value read a property changed earlier in the same loop iteration?
5. Should the operation be wrapped in an explicit transaction, audit record, or reviewed suppression?

## CI Severity Starter

Start with filter safety as a warning, then promote it once the team agrees on bulk-write policy:

```ini
[*.cs]

# Set-based write opportunities
dotnet_diagnostic.LC012.severity = suggestion
dotnet_diagnostic.LC032.severity = suggestion

# Bulk write safety
dotnet_diagnostic.LC035.severity = warning
```

Use the [EF Core query analyzer CI guide](/LinqContraband/ef-core-query-analyzer-ci/) when these diagnostics should run
on every pull request. Use the [EF Core query performance checklist](/LinqContraband/ef-core-query-performance-checklist/)
when reviewers need the broader query-performance context.

## Official Links

- Canonical repository: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Official NuGet package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Full rule catalog: [georgepwall1991.github.io/LinqContraband/rule-catalog.html](https://georgepwall1991.github.io/LinqContraband/rule-catalog.html)
- Safe install guidance: [Official LinqContraband downloads and authenticity](/LinqContraband/security-and-authenticity/)
