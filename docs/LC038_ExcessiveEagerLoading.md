---
layout: default
title: "LC038: Excessive Eager Loading"
---

# LC038: Excessive Eager Loading

## What it flags

Flags EF Core query chains that use too many `Include(...)` / `ThenInclude(...)` calls on the same provable query root.

By default the threshold is 4 include steps. Teams can raise or lower it with:

```ini
dotnet_code_quality.LC038.include_threshold = 4
```

Invalid, missing, or non-positive values fall back to the default threshold.

## Why it matters

Large eager-loading graphs often over-fetch related data, create wide joined result shapes, duplicate rows before materialization, and make query behaviour harder to reason about. Even when `AsSplitQuery()` avoids cartesian blow-ups, the query can still load more data than the caller needs.

LC038 is intentionally advisory. Sometimes a large Include graph is exactly the right shape for a screen or export. The diagnostic is a review prompt, not proof of a bug.

## The crime

```csharp
var customers = db.Customers
    .Include(c => c.Address)
    .ThenInclude(a => a.Country)
    .ThenInclude(c => c.Region)
    .ThenInclude(r => r.Continent)
    .ToList();
```

## Better shapes

Project the exact result when the caller only needs a DTO or scalar values:

```csharp
var customers = db.Customers.Select(c => new CustomerSummary
{
    Id = c.Id,
    Country = c.Address.Country.Name,
    OpenOrderCount = c.Orders.Count(o => o.IsOpen)
});
```

Split unrelated work into separate queries when different parts of the graph are used independently:

```csharp
var customer = await db.Customers
    .Include(c => c.Address)
    .SingleAsync(c => c.Id == id);

var recentOrders = await db.Orders
    .Where(o => o.CustomerId == id)
    .OrderByDescending(o => o.CreatedAt)
    .Take(20)
    .ToListAsync();
```

Keep the Include graph when the caller genuinely needs the full aggregate and the cost is acceptable. In that case, consider `AsSplitQuery()`, query tags, pagination, and explicit suppressions so future readers know the large load was intentional.

## What it counts

LC038 counts EF Core `Include` and `ThenInclude` calls in one chain once the chain is rooted in a provable EF query:

- `DbSet<T>` properties and fields.
- `DbContext.Set<T>()`.
- Transparent query shapers before the Include chain, such as `Where`, ordering, `Skip`, `Take`, `AsNoTracking`, `AsNoTrackingWithIdentityResolution`, `AsTracking`, `AsSplitQuery`, `AsSingleQuery`, and `TagWith`.

It does not guess through arbitrary helper methods or projected shapes.

## What it does not flag

LC038 does not flag non-EF Include lookalikes, dynamic helper APIs, or query shapes where the EF root cannot be proven. It also does not decide whether the Include graph is wrong; the correct action depends on the caller, cardinality, and provider behaviour.

## Relationship to LC006

LC006 looks for sibling collection Includes that can cause cartesian explosion. LC038 is broader and lower severity: it flags a high Include count even when the risk is simply over-fetching or review complexity.

## Manual review checklist

1. Does the caller use every included navigation?
2. Would a projection be clearer and cheaper?
3. Are unrelated branches better loaded as separate queries?
4. Does `AsSplitQuery()` help, or does it only hide the fact that too much data is loaded?
5. Is a suppression warranted because the full aggregate is intentionally loaded?
