---
layout: default
title: "LC007: N+1 Database Execution Inside Loop"
description: "LC007 finds EF Core N+1 queries: Find, ToList, Count and other database calls that provably run once per loop iteration instead of once."
---

# LC007: N+1 Database Execution Inside Loop

## In Plain Terms

Imagine you need 10 eggs. You drive to the store, buy *one* egg, drive home.
Drive back, buy *one* egg, drive home. You do this 10 times. You spend all day driving instead of just buying the carton
at once.

## Goal
Catch EF Core database execution that is provably performed once per loop iteration.

## What LC007 Reports

### 1. Direct EF lookups inside loops
`Find` and `FindAsync` on `DbSet<T>` are reported when they execute inside `for`, `foreach` (including the deconstruction form `foreach (var (a, b) in …)`), `await foreach`, `while`, or `do` loops.

```csharp
foreach (var id in ids)
{
    var user = db.Users.Find(id);
}
```

### 2. Explicit loading inside loops
`Reference(...).Load/LoadAsync` and `Collection(...).Load/LoadAsync` are reported because they issue a separate database operation per entity.

```csharp
foreach (var user in db.Users.ToList())
{
    db.Entry(user).Collection(u => u.Orders).Load();
}
```

### 3. Query materialization, aggregates, and set-based executors on proven EF sources
LC007 reports materializers and executors such as `ToList`, `Count`, `Any`, `ExecuteDelete`, and `ExecuteUpdate` when the query origin is provably EF-backed.

```csharp
foreach (var user in db.Users.ToList())
{
    var orderCount = db.Entry(user).Collection(u => u.Orders).Query().Count();
}

while (queue.TryDequeue(out var userId))
{
    db.Users.Where(u => u.Id == userId).ExecuteDelete();
}
```

The analyzer follows direct EF roots such as `DbSet<T>`, `DbContext.Set<T>()`, navigation `Query()` calls, and single-assignment query local hops when the origin stays provable.
Deferred `AsEnumerable()` boundaries before terminal execution still report when the upstream source is provably EF-backed.
When a query materializer is used as the source of an inner `foreach`, LC007 still reports if that inner loop sits inside another loop and the source is re-executed once per outer iteration.

### 4. Helper methods that run the query
The most common hidden N+1 is a loop that calls a helper in the same project which runs the query. LC007 reads the helper's body and reports the call in the loop:

```csharp
foreach (var order in orders)
{
    var customer = await GetCustomerAsync(order.CustomerId, ct);
    // LC007: 'GetCustomerAsync' runs 'FirstOrDefaultAsync' on every iteration of the loop
}

private Task<Customer?> GetCustomerAsync(int id, CancellationToken ct) =>
    _db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
```

This covers ordinary and static methods, extension methods and local functions whose source is in the project being analyzed, followed up to three calls deep (loop → `A` → `B` → `C` running the query). The helper's execution must be one LC007 would report if it were written in the loop: `Find`, an explicit load, or a materializer or executor on a provably EF-backed source such as `_db.Customers` or `db.Set<T>()`. The loop exemptions below (batch, drain, polling, retry, `Chunk`, level-by-level loops) apply at the call site exactly as they do to a direct query, and a `while`, `do` or `for` loop whose condition runs a query through a helper (`while (await HasPendingAsync())`) counts as a drain loop.

LC007 stays quiet on a helper call when:
- the helper is overridable: `virtual`, `abstract` or interface dispatch, unless the method or its type is `sealed` or `static`, since an override might not query;
- the helper exists only as metadata, or its source belongs to another project;
- the query sits in a lambda or local function the helper only declares, such as a cache factory `_cache.GetOrCreateAsync(key, _ => db.Customers.FirstAsync(...))`;
- the helper is memoized: it calls a cache API (`TryGetValue`, `ContainsKey`, `GetOrAdd`, `GetOrCreate`, any `*Cache*` type), assigns with `??=` or reads with `??` a field or property, or returns early from an `if` that reads a field or property of its own type before the query;
- the helper's query is inside a loop of its own (reported there once, or exempt there as a batch loop);
- the helper only builds and returns an `IQueryable`. Executing that result in the loop (`Children(id).Count()`) is not traced back into the helper;
- the query source is a parameter (`IQueryable<T>`, `DbSet<T>` or a `List<T>`), which LC007 cannot prove is EF-backed.

The code fix is not offered for helper calls.

## What LC007 Intentionally Ignores
- Plain LINQ-to-Objects or `AsQueryable()` sources
- `AsEnumerable()` aggregates over already in-memory collections
- Aggregates, lookups, or filters over already materialized `List<T>`/array/local DTO collections inside loops
- Ambiguous `IQueryable` provenance through parameters, fields, properties, or multi-assignment locals
- Query construction inside loops when no execution method is invoked
- `Reference(...)` and `Collection(...)` access without `Load`, `LoadAsync`, or `Query()` execution
- Invocations nested inside lambdas or local functions declared in the loop body (a call to such a local function from the loop is a helper call, see above)
- Loop-source materialization that happens once before iteration, such as the `db.Users.ToList()` part of a `foreach`
- `while`, `do` and `for` loops that run until the database says they are done, as long as the loop condition does not walk an item source of its own (see below)

### Batch, polling and retry loops

A loop that queries until the data runs out issues one query per batch, not one per item, so LC007 stays quiet on it. This covers keyset and paged batching, batched deletes, drain loops, `BackgroundService` polling and catch-guarded retries:

```csharp
var lastId = 0;
while (true)
{
    var batch = await db.Orders.Where(o => o.Id > lastId).OrderBy(o => o.Id).Take(500).ToListAsync(ct);
    if (batch.Count == 0) break;           // the batch decides when the loop ends
    lastId = batch[^1].Id;
    Process(batch);
}

while (await db.Outbox.Take(1000).ExecuteDeleteAsync(ct) > 0) { }

while (!ct.IsCancellationRequested)
{
    Dispatch(await db.Outbox.Where(m => m.SentAt == null).ToListAsync(ct));
    await Task.Delay(TimeSpan.FromSeconds(5), ct);
}
```

A `while`, `do` or `for` loop counts as one of these when:
- the query result decides when the loop stops: the execution sits in the loop condition, or its result (or a local computed from it, such as `hasMore = batch.Count == size`) is read by the condition or by an `if` that breaks out of the loop or returns;
- the loop condition itself queries the database (`while (await db.Jobs.AnyAsync(...))`);
- the loop body waits with `Task.Delay` or `Thread.Sleep`, or the condition waits on `PeriodicTimer.WaitForNextTickAsync`; or
- the execution sits in a `try` with a `catch`, and the `try` breaks out of the loop or returns after it succeeds; or
- the query pages by a counter the loop advances: its `Skip(...)` reads the counter (`Skip(page * size)`, or an `offset` the body increases) and its `Take(...)` reads more than one row; or
- the loop is a batch drain loop (`while` or `do` only): another query in the same iteration is bounded by `Take(n)` with `n` other than 0 or 1, and its result ends the loop as in the first point, such as the row count an `ExecuteDelete` returns or the length, count or emptiness of the materialized batch. Every query in that iteration, including the delete or update that works on the batch, then runs once per batch.

```csharp
var found = int.MaxValue;
while (found >= options.BatchSize)
{
    var query = db.PersistedGrants.Where(g => g.Expiration < now).OrderBy(g => g.Expiration);
    var expired = await query.Take(options.BatchSize).AsNoTracking().ToArrayAsync(ct);
    found = expired.Length;                 // a short batch ends the loop
    if (found > 0)
    {
        await query.Where(g => g.Expiration <= expired[^1].Expiration).ExecuteDeleteAsync(ct); // quiet: one per batch
    }
}
```

The exemption only applies when the loop condition reads nothing but constants, integer counters (including an integer setting such as `_options.BatchSize`, read through fields and properties that are not collections), `bool` flags, cancellation, database executions and values derived from the query result. A condition that walks items of its own, such as `queue.TryDequeue(out var id)`, `reader.Read()` or `i < ids.Length`, still reports, even when the loop also breaks on the result. Queries in a `foreach` report, except over `Chunk(...)` as below, and so does a query inside a batch loop that sits in an outer per-item loop.

A `foreach` over `ids.Chunk(n)` is the usual way to keep an `IN` list under the database's parameter limit, and it stays quiet when the query reads the whole chunk: `batch.Contains(...)`, the chunk passed as an argument, or a local built from it such as `batch.ToHashSet()`:

```csharp
foreach (var batch in seriesIds.Chunk(500))
{
    var series = await db.Series.Where(s => batch.Contains(s.Id)).ToListAsync(ct); // one query per chunk
}
```

A query that reads one element of the chunk (`batch[0]`, `batch.First()`) or runs inside a nested `foreach (var id in batch)` still reports.

### Level-by-level hierarchy walks

Walking a tree one level per query is the usual way to avoid a query per node, so LC007 stays quiet on it too:

```csharp
var frontier = new List<Guid> { rootId };
while (frontier.Count > 0)
{
    var next = await db.Folders
        .Where(f => frontier.Contains(f.ParentId))   // the whole level in one query
        .Select(f => f.Id)
        .ToListAsync(ct);
    frontier = new List<Guid>();
    foreach (var id in next)
        if (visited.Add(id)) frontier.Add(id);      // the next level comes from the result
}
```

This applies when the loop condition reads a collection that the loop refills from the query result (`Add`, `AddRange`, `Enqueue`, `Push` or `UnionWith` with values from it, or an assignment), and the query uses that collection only as a whole: `frontier.Contains(...)`, or `frontier` passed as an argument, directly or through locals the query is built from. A worklist that takes one item at a time still reports, because it runs one query per node:

```csharp
while (pending.Count > 0)
{
    var id = pending.Dequeue();
    var children = db.Folders.Where(f => f.ParentId == id).Select(f => f.Id).ToList(); // LC007
    foreach (var child in children) pending.Enqueue(child);
}
```

## Fixer Behavior
LC007 offers a fixer only for conservative, analyzer-proven explicit-loading cases.

- It rewrites unconditional strongly-typed `Reference(...).Load/LoadAsync` and `Collection(...).Load/LoadAsync` inside `foreach` or `await foreach` loops to eager loading with `Include(...)`.
- It updates the loop source query and removes the per-item load statement.
- It does not offer a fix for helper-method calls, string-based navigation access, `Find`, aggregates, `Query().Count()`, filtered navigation queries, conditional loads, or control-flow-heavy loops.

## Example Fix

```csharp
// Before
foreach (var user in db.Users.ToList())
{
    db.Entry(user).Collection(u => u.Orders).Load();
    Console.WriteLine(user.Id);
}

// After
foreach (var user in db.Users.Include(u => u.Orders).ToList())
{
    Console.WriteLine(user.Id);
}
```

## Metadata

### ID: `LC007`
### Category: `Performance`
### Severity: `Warning`
