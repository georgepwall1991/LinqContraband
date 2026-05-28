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

`Chunk(size)` is **not** treated as a bounding operator: there is no `Queryable.Chunk`, so `db.Users.Chunk(1000).ToList()` binds to `Enumerable.Chunk` and materializes the entire table before partitioning it — the `size` argument bounds the chunk size, not the number of rows fetched. Add a real row limit such as `Take` or `Skip`/`Take` pagination before chunking if the source can be large. (A `Where` filter narrows the result but does not cap the row count, so LC031 still reports a filtered materialization that has no `Take`.)

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
