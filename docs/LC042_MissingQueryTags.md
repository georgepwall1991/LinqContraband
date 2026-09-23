---
layout: default
title: "LC042: Missing Query Tags"
description: "LC042 flags complex EF Core queries without TagWith() or TagWithCallSite(), so the SQL they produce is hard to trace back to code in logs."
---

# LC042: Missing Query Tags

## In Plain Terms

Imagine a giant pile of lunch boxes with no names on them. When something
goes wrong, nobody knows which lunch belongs to whom.

## Goal

Detect complex EF Core queries that run without `TagWith(...)` or `TagWithCallSite()`.

## The Problem

A tagged query carries a SQL comment naming where it came from. When a slow query shows up in logs, a profiler, or the database's query store, the tag leads straight to the code. Simple lookups are easy to recognize without one. Queries that filter, sort, page, join, or group are not.

### Example Violation

```csharp
var users = db.Users
    .Where(u => u.IsActive)
    .OrderBy(u => u.Name)
    .Take(10)
    .ToList();
```

## The Fix

```csharp
var users = db.Users
    .Where(u => u.IsActive)
    .OrderBy(u => u.Name)
    .Take(10)
    .TagWith("Users.ActiveByName")
    .ToList();
```

`TagWithCallSite()` (EF Core 6+) tags the query with the file and line instead.

## Analyzer Logic

### ID: `LC042`
### Category: `Performance`
### Severity: `Info`

1. Start at a method that runs the query: `ToList`, `ToArray`, `ToDictionary`, `ToHashSet`, `First`/`Single`/`Last` (and their `OrDefault` forms), `Any`, `All`, `Count`, `LongCount`, `Sum`, `Min`, `Max`, `Average`, and their EF Core `Async` versions.
2. Walk back to the query root and score each operator:

   | Operator | Score |
   | --- | --- |
   | `Join`, `GroupJoin`, `LeftJoin`, `RightJoin`, `GroupBy`, `SelectMany` | 2 |
   | Every other `Queryable` operator (`Where`, `Select`, `OrderBy`, `ThenBy`, `Skip`, `Take`, `Distinct`, ...) and `Include` / `ThenInclude` | 1 |
   | A predicate or selector passed to the terminal method, as in `Count(o => o.IsOpen)` | 1 |
   | `AsNoTracking`, `AsNoTrackingWithIdentityResolution`, `AsTracking`, `AsSplitQuery`, `AsSingleQuery`, `IgnoreQueryFilters`, `IgnoreAutoIncludes`, `AsQueryable` | 0 |

3. Report on the terminal method when the chain has no tag, starts at a `DbSet` or `DbContext.Set<T>()`, and scores at least the threshold (3 by default).

Query syntax (`from ... join ... where ... select`) is scored the same way as the method chain it compiles to.

### Configuration

```ini
dotnet_code_quality.LC042.query_operator_threshold = 3
```

## When it stays quiet (non-goals)

- A `TagWith` or `TagWithCallSite` anywhere in the chain.
- Queries under the threshold, such as `Where(...).Select(...)`. Tracking and split-query options do not add to the score.
- Subqueries inside an outer query's lambda, such as `db.Customers.Where(c => db.Orders.Any(...))`. The outer query is scored and tagged as a whole.
- Chains that pass through a method the rule does not know, such as a repository helper. The helper may add its own tag.
- In-memory sources, including `AsQueryable()` over a list, and LINQ to Objects after `AsEnumerable()`.
- Queries built up in a local variable and run later. Only one fluent chain is scored.

## Code Fix

Two fixes insert a tag right before the method that runs the query:

- **Tag query with "Type.Member"** adds `TagWith("Type.Member")`, named after the method, property, or constructor that contains the query. Queries in lambdas and local functions use the enclosing member's name.
- **Tag query with its call site** adds `TagWithCallSite()`. It is offered only when the referenced EF Core version has it (6.0 and later).

```csharp
// Before
var users = db.Users.Where(u => u.IsActive).OrderBy(u => u.Name).Take(10).ToList();

// After
var users = db.Users.Where(u => u.IsActive).OrderBy(u => u.Name).Take(10).TagWith("UserService.GetActive").ToList();
```

In a multi-line chain the tag goes on its own line. The fix adds `using Microsoft.EntityFrameworkCore;` when the tag method is not already in scope. The static form (`Enumerable.ToList(query)`) is reported without a fix. Rename the generated tag if your team uses a different convention; the rule only checks that a tag exists.

## Test Cases

### Violations

```csharp
db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).ToList();
db.Orders.Join(db.Customers, o => o.CustomerId, c => c.Id, (o, c) => c.Name).Where(n => n != null).ToList();
await db.Orders.Where(o => o.Total > 0).Distinct().CountAsync(o => o.CustomerId == 1);
```

### Valid

```csharp
db.Orders.AsNoTracking().Where(o => o.Total > 0).Select(o => o.Id).ToList();
db.Orders.Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).TagWith("orders").ToList();
cachedOrders.AsQueryable().Where(o => o.Total > 0).OrderBy(o => o.Id).Take(10).ToList();
```
