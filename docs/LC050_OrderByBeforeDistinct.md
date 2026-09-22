---
layout: default
title: "LC050: OrderBy before Distinct is discarded"
description: "LC050 flags EF Core queries that sort before Distinct(), where SQL DISTINCT silently drops the ORDER BY, and moves the sort after Distinct()."
---

# LC050: OrderBy before Distinct is discarded

## In Plain Terms

You line people up by height, then tell them to remove duplicates by forming a crowd. Whatever order they had is gone. If you wanted them sorted, you have to line them up after the crowd forms.

## Goal

Detect EF Core queries that call `OrderBy` / `OrderByDescending` and then `Distinct()` with nothing in between that needs the order.

## The Problem

SQL `DISTINCT` does not preserve row order. When an EF Core query sorts and then calls `Distinct()`, EF Core removes the `ORDER BY` from the generated SQL. The query runs without an error, and the results come back in whatever order the database picks. That order often looks sorted in development and changes with data volume, indexes, or query plans in production.

```csharp
// Violation: the ORDER BY is dropped, so the names are not sorted.
var names = await db.Customers
    .OrderBy(c => c.Name)
    .Select(c => c.Name)
    .Distinct()
    .ToListAsync();
```

## The Fix

Sort after `Distinct()`:

```csharp
var names = await db.Customers
    .Select(c => c.Name)
    .Distinct()
    .OrderBy(name => name)
    .ToListAsync();
```

When the query keeps whole rows, move the sort chain as it is:

```csharp
// Before
var orders = db.Orders.OrderByDescending(o => o.PlacedAt).ThenBy(o => o.Id).Distinct();

// After
var orders = db.Orders.Distinct().OrderByDescending(o => o.PlacedAt).ThenBy(o => o.Id);
```

If the sort key is not part of the projection, you cannot sort the distinct values by it directly. Group instead, for example `GroupBy(o => o.Customer.Name).OrderBy(g => g.Min(o => o.PlacedAt)).Select(g => g.Key)`.

## Analyzer Logic

### ID: `LC050`
### Category: `Correctness`
### Severity: `Warning`

1. Start at a parameterless `Queryable.Distinct()` (fluent, static, or after query syntax).
2. Walk back through operators that neither need nor keep the order: `ThenBy`/`ThenByDescending`, `Where`, `Select`, and the EF Core pass-throughs `AsNoTracking`, `AsNoTrackingWithIdentityResolution`, `AsTracking`, `TagWith`, `TagWithCallSite`, `IgnoreQueryFilters`, `IgnoreAutoIncludes`, `Include`, `ThenInclude`, `AsSplitQuery`, and `AsSingleQuery`.
3. Report when the walk reaches `OrderBy` or `OrderByDescending`. The diagnostic sits on `Distinct` and names the discarded sort.

## When it stays quiet (non-goals)

- `Skip`, `Take`, or any other operator between the sort and `Distinct()`. Those keep the sort meaningful because it decides which rows reach `Distinct()`.
- `Distinct(comparer)`, which EF Core cannot translate anyway.
- LINQ to Objects (`List<T>.OrderBy(...).Distinct()`) and `AsQueryable()` over in-memory data, where `Distinct` keeps first-seen order.
- `GroupBy`, `Join`, `SelectMany`, and other shape-changing operators between the sort and `Distinct()`.
- Sorted queries stored in a local and made distinct later. Only fluent chains are analyzed.

## Code Fix

The fixer handles two shapes and reports everything else without a fix:

- A sort chain directly before `Distinct()`: `q.OrderBy(k).ThenBy(k2).Distinct()` becomes `q.Distinct().OrderBy(k).ThenBy(k2)`.
- A single sort whose key is exactly the projected value: `q.OrderBy(o => o.Name).Select(x => x.Name).Distinct()` becomes `q.Select(x => x.Name).Distinct().OrderBy(x => x)`.

After the fix the expression is an `IOrderedQueryable<T>`. When the query initializes a `var` local that is later reassigned, the new type would break that assignment, so no fix is offered there. Query-syntax `orderby` clauses and chains with `Where` between the sort and `Distinct()` are also reported without a fix.

## Test Cases

### Violations

```csharp
db.Orders.OrderBy(o => o.PlacedAt).Distinct();
db.Orders.OrderBy(o => o.Total).Where(o => o.Total > 0).Select(o => o.Total).Distinct();
(from c in db.Customers orderby c.Name select c.Name).Distinct();
```

### Valid

```csharp
db.Customers.Select(c => c.Name).Distinct().OrderBy(n => n);
db.Orders.OrderBy(o => o.PlacedAt).Take(10).Distinct();
cachedOrders.OrderBy(o => o.PlacedAt).Distinct();
```
