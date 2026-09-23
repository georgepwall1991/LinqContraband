---
layout: default
title: "LC031: Unbounded Query Materialization"
description: "LC031 flags EF Core queries materialized with no filter or Take limit, which can load an entire table into memory as the data grows."
---

# LC031: Unbounded Query Materialization

## In Plain Terms

Imagine you go to a library and say "Give me every book." The librarian
starts piling books onto a cart — thousands and thousands of them. Your arms break. You only needed the first 10! You
should have said "Give me the first 10 books" instead.

## What it flags

Flags materialization of an apparently unbounded query because loading an entire table or broad result set usually indicates missing filters, missing pagination, or an accidental scan.

Collection materializers include `ToList()`, `ToArray()`, `ToDictionary()`, `ToHashSet()`, `ToLookup()`, and the async EF variants where EF provides one.

The rule reports the materializer that runs the query. In `db.Orders.ToList().Where(o => o.Total > 1).ToList()` or `(await db.Orders.ToListAsync()).Where(...).ToList()` the table is loaded once, at the inner `ToList()`/`ToListAsync()`, and only that call is reported. The outer materializer copies a list that is already in memory, so it is not reported again, and neither is a later `ToList()` on a local that holds the loaded list.

## Why it matters

LinqContraband reports this rule when the query shape suggests a risky or non-translatable pattern that is better made explicit before it reaches production.

## Typical fix

Add a real row bound, usually `Take`, ordered `Skip`/`Take` pagination, keyset/cursor pagination, or a single-row terminal such as `FirstOrDefault`. A `Where` filter is often still useful, but it narrows the result set without capping it.

LC031 follows direct DbSet query chains, `DbContext.Set<TEntity>()` query chains, query-syntax expressions, and simple single-assignment local aliases:

```csharp
var query = db.Users.Where(user => user.IsActive);
var users = query.ToList(); // LC031

var activeUsers =
    (from user in db.Users
     where user.IsActive
     select user).ToList(); // LC031
```

It stays silent on bounded aliases and ambiguous reassigned locals rather than guessing which query shape reaches the materializer.

## What counts as bounded

- `Take`, `First`, `Single`, `Last`, `Find` and their async forms, applied while the source is still a query.
- A primary-key lookup: `Where(u => u.Id == id)` (also with `&&` extra conditions, or with the operands swapped) or `Where(u => ids.Contains(u.Id))`. The key is a property named `Id`, `<EntityName>Id`, or marked `[Key]`. Foreign keys (`OrderId` on `OrderLine`) and `||` conditions can still match many rows and keep reporting.

LC031 walks back through LINQ (`System.Linq`) and EF Core operators only. A project's own `IQueryable` helper, such as a `Paginate(page, size)` extension or an Ardalis-style `WithSpecification(spec)`, may apply the bound itself, so LC031 stops there and stays quiet:

```csharp
var page = await db.Orders.Paginate(pageIndex, 50).ToListAsync(); // no LC031
```

A helper that takes and returns `IEnumerable<T>` runs after the full load, so the rule looks past it and still reports.

## What does not count as a bound

These operators or query options do not prove a capped result set:

- `Where(...)`: narrows matching rows, but can still match every row.
- `OrderBy(...)`: changes order only.
- `Skip(...)` without a later `Take(...)`: can still load every row after the skipped prefix.
- `TakeLast(...)` / `SkipLast(...)`: EF Core cannot translate these as bounded server-side operations for normal relational queries.
- `Chunk(size)`: the `size` argument bounds each returned chunk, not the total number of rows fetched. Do not treat `Chunk` as pagination; put `Take` or ordered `Skip`/`Take` before chunking if the source can be large.
- Query options such as `AsNoTracking()`, `AsTracking()`, `AsSplitQuery()`, or `AsSingleQuery()`: useful options, but not row-count bounds.
- `Take(...)` after `AsEnumerable()`: the whole table is loaded and then trimmed in memory. `db.Users.AsEnumerable().Take(10).ToList()` reports.

Add the limit before crossing to LINQ-to-Objects or materializing:

```csharp
var page = await db.Users
    .Where(user => user.IsActive)
    .OrderBy(user => user.Id)
    .Skip(pageIndex * pageSize)
    .Take(pageSize)
    .ToListAsync();
```

## Intentional full scans

LC031 has no automatic fixer because the correct remediation is product-specific. A UI list usually needs pagination; an export may need a background job, streaming, batching, or a reviewed suppression; a maintenance path may need an explicit operational limit. The analyzer cannot safely choose between those designs.

When the full scan is intentional, keep the code explicit and document the reason close to the query, for example with a narrow suppression around an export or backfill path.

## Samples

See `samples/LinqContraband.Sample/Samples/LC031_UnboundedQueryMaterialization/` for a focused example.

## The crime

```csharp
var allOrders = await db.Orders.ToListAsync();
```

## A better shape

```csharp
var recentOrders = await db.Orders
    .Where(o => o.CreatedAt >= cutoff)
    .OrderByDescending(o => o.CreatedAt)
    .Take(200)
    .ToListAsync();
```
