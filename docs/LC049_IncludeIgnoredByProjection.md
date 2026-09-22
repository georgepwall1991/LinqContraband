---
layout: default
title: "LC049: Include is ignored by a Select projection"
description: "LC049 flags EF Core Include calls that a later Select projection makes EF Core ignore, and removes them."
---

# LC049: Include is ignored by a Select projection

## In Plain Terms

You ask the kitchen to bring the whole meal with every side dish, then say "actually, just tell me the price". The side dishes never leave the kitchen. The order slip still says "with sides", though, and the next person to read it will believe they are coming.

## Goal

Detect `Include` / `ThenInclude` on an EF Core query whose `Select` projects the root entity into scalars, DTOs, or anonymous types.

## The Problem

EF Core applies `Include` only to entity instances the query returns. Once a `Select` turns `Order` into `new { o.Id, o.Customer.Name }` or `new OrderDto { ... }`, EF Core drops the `Include` without a warning. It loads nothing extra and costs nothing at runtime, but it is dead code that lies: reviewers read it as "customers are eager-loaded here", and people copy it into queries where it does matter.

The projection already decides which related columns are read. `o.Customer.Name` inside the `Select` becomes a SQL join on its own.

```csharp
// Violation: the Include is ignored because the query returns anonymous objects, not Orders.
var rows = await db.Orders
    .Include(o => o.Customer)
    .Include(o => o.Lines).ThenInclude(l => l.Product)
    .Select(o => new
    {
        o.Id,
        CustomerName = o.Customer.Name,
        Skus = o.Lines.Select(l => l.Product.Sku).ToList()
    })
    .ToListAsync();
```

## The Fix

Delete the `Include` and any `ThenInclude` calls chained to it. The query's SQL does not change.

```csharp
var rows = await db.Orders
    .Select(o => new
    {
        o.Id,
        CustomerName = o.Customer.Name,
        Skus = o.Lines.Select(l => l.Product.Sku).ToList()
    })
    .ToListAsync();
```

If you meant to load full entities, remove the `Select` instead and keep the `Include`.

## Analyzer Logic

### ID: `LC049`
### Category: `Performance`
### Severity: `Info`

1. Start at a `Queryable.Select` whose selector is a lambda over an entity-like element type.
2. Walk back through the fluent chain while the operators keep returning the same entities: `Where`, `OrderBy`/`ThenBy` (and descending), `Skip`, `Take`, `Distinct`, `Reverse`, `ThenInclude`, `AsNoTracking`, `AsNoTrackingWithIdentityResolution`, `AsTracking`, `TagWith`, `TagWithCallSite`, `IgnoreQueryFilters`, `IgnoreAutoIncludes`, `AsSplitQuery`, and `AsSingleQuery`. Any other operator stops the walk.
3. Collect every EF Core `Include` (lambda, filtered-lambda, and string overloads from `Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions`) on that element type.
4. Report each one, unless the projection could still return an entity. An entity value (the lambda parameter, a navigation reached from it, or a LINQ operator that passes entities through) is allowed only as:
   - the instance of a member access (`o.Customer.Name`),
   - one side of a `null` comparison (`o.Customer != null ? o.Customer.Name : null`),
   - the source of a LINQ operator whose own result is checked in turn (`o.Lines.Count()`, `o.Lines.Select(l => new LineDto { Sku = l.Sku })`).

Anything else keeps the rule quiet. Objects the projection constructs itself, such as DTOs and anonymous types, are not entity values even when their type is a user class.

## When it stays quiet (non-goals)

- `Select(o => o)`, `Select(o => new { Order = o })`, or any projection that returns the root entity somewhere in its shape.
- Projections that return a navigation entity or collection, such as `Select(o => o.Customer)`, `new { o.Lines }`, or `o.Lines.Select(l => l.Product)`. EF Core carries the matching `ThenInclude` onto those entities, so the Include can still matter.
- Entities passed to a method, `Select(o => Map(o))`, because the helper may return the entity or read its navigations client-side.
- `AsEnumerable()` or any other client-side boundary before the `Select`, because the entities are materialized with their includes first.
- `GroupBy`, `Join`, `SelectMany`, or other shape-changing operators between the `Include` and the `Select`.
- Includes stored in a local and projected later (`var q = db.Orders.Include(...); q.Select(...)`). Only fluent chains are analyzed.
- Lookalike `Include` methods outside `Microsoft.EntityFrameworkCore`.

## Code Fix

The fixer removes the reported `Include` together with the `ThenInclude` calls chained to it, and keeps every other operator in place. Fix All removes every ignored `Include` in a chain in one pass. The static form `EntityFrameworkQueryableExtensions.Include(source, ...)` is reported without a fix.

## Test Cases

### Violations

```csharp
db.Orders.Include(o => o.Customer).Select(o => o.Id);
db.Orders.Include("Lines").Select(o => new { o.Id, Count = o.Lines.Count() });
db.Orders.AsNoTracking().Include(o => o.Customer).Select(o => new OrderRow(o.Id, o.Customer.Name));
```

### Valid

```csharp
db.Orders.Include(o => o.Customer).ToList();
db.Orders.Include(o => o.Customer).Select(o => o.Customer);
db.Orders.Include(o => o.Customer).AsEnumerable().Select(o => o.Customer.Name);
```
