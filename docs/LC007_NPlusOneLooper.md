# LC007: Database Execution Inside Loop

## Goal
Catch EF Core database execution that is provably performed once per loop iteration.

## What LC007 Reports

### 1. Direct EF lookups inside loops
`Find` and `FindAsync` on `DbSet<T>` are reported when they execute inside `for`, `foreach`, `await foreach`, `while`, or `do` loops.

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

while (hasWork)
{
    db.Users.Where(u => u.Inactive).ExecuteDelete();
}
```

The analyzer follows direct EF roots such as `DbSet<T>`, `DbContext.Set<T>()`, navigation `Query()` calls, and single-assignment query local hops when the origin stays provable.

## What LC007 Intentionally Ignores
- Plain LINQ-to-Objects or `AsQueryable()` sources
- Aggregates, lookups, or filters over already materialized `List<T>`/array/local DTO collections inside loops
- Ambiguous `IQueryable` provenance through parameters, fields, properties, or multi-assignment locals
- Query construction inside loops when no execution method is invoked
- `Reference(...)` and `Collection(...)` access without `Load`, `LoadAsync`, or `Query()` execution
- Invocations nested inside lambdas or local functions declared in the loop body
- Loop-source materialization that happens once before iteration, such as the `db.Users.ToList()` part of a `foreach`

## Fixer Behavior
LC007 offers a fixer only for conservative, analyzer-proven explicit-loading cases.

- It rewrites unconditional strongly-typed `Reference(...).Load/LoadAsync` and `Collection(...).Load/LoadAsync` inside `foreach` or `await foreach` loops to eager loading with `Include(...)`.
- It updates the loop source query and removes the per-item load statement.
- It does not offer a fix for string-based navigation access, `Find`, aggregates, `Query().Count()`, filtered navigation queries, conditional loads, or control-flow-heavy loops.

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
