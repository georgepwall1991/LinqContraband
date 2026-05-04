# LC031: Unbounded Query Materialization

## What it flags

Flags materialization of an apparently unbounded query because loading an entire table or broad result set usually indicates missing filters, missing pagination, or an accidental scan.

## Why it matters

LinqContraband reports this rule when the query shape suggests a risky or non-translatable pattern that is better made explicit before it reaches production.

## Typical fix

Add a filter, a Take limit, pagination, or make the intentional full scan explicit and well-documented.

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
