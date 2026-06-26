---
layout: default
title: "LC031: Unbounded Query Materialization"
---

# LC031: Unbounded Query Materialization

## What it flags

Flags materialization of an apparently unbounded query because loading an entire table or broad result set usually indicates missing filters, missing pagination, or an accidental scan.

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

## What does not count as a bound

These operators or query options do not prove a capped result set:

- `Where(...)`: narrows matching rows, but can still match every row.
- `OrderBy(...)`: changes order only.
- `Skip(...)` without a later `Take(...)`: can still load every row after the skipped prefix.
- `TakeLast(...)` / `SkipLast(...)`: EF Core cannot translate these as bounded server-side operations for normal relational queries.
- `Chunk(size)`: the `size` argument bounds each returned chunk, not the total number of rows fetched. Do not treat `Chunk` as pagination; put `Take` or ordered `Skip`/`Take` before chunking if the source can be large.
- Query options such as `AsNoTracking()`, `AsTracking()`, `AsSplitQuery()`, or `AsSingleQuery()`: useful options, but not row-count bounds.

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
