# LC024: GroupBy with Non-Translatable Projection

## What it flags

Flags `IQueryable.GroupBy(...)` pipelines that project values EF Core cannot translate cleanly to SQL, which usually triggers runtime failures or accidental client-side grouping.

## Why it matters

LinqContraband reports this rule when the query shape suggests a risky or non-translatable pattern that is better made explicit before it reaches production.

## Typical fix

Move the projection before the grouping only when it stays translatable, aggregate on the server, or materialize intentionally before performing complex grouping in memory.

LC024 is intentionally manual-only. The safe rewrite depends on whether the caller wants a server aggregate, a pre-grouped projection, or explicit client-side grouping after materialization.

## Samples

See `samples/LinqContraband.Sample/Samples/LC024_GroupByNonTranslatable/` for a focused example.

## The crime

```csharp
var query = db.Orders
    .GroupBy(o => o.CustomerId)
    .Select(g => new
    {
        g.Key,
        Items = g.ToList()
    });
```

Other risky shapes include calling local helpers over `g.Key`, using client-only string comparison overloads in the grouped projection, constructing objects from `g` directly, invoking local helpers named like aggregates with `g`, or nesting non-aggregate group access inside another projection object.

## A better shape

```csharp
var query = db.Orders
    .GroupBy(o => o.CustomerId)
    .Select(g => new
    {
        g.Key,
        Count = g.Count(),
        Total = g.Sum(o => o.Total)
    });
```

LC024 applies to fluent `GroupBy(...).Select(...)` and query-syntax `group ... by ... into g select ...` projections. It stays quiet for aggregate-only projections (`Key`, `Count`, `LongCount`, `Sum`, `Average`, `Min`, `Max`) and for LINQ-to-Objects grouping where the source is already `IEnumerable<T>`.

Aggregate exemptions apply only to known LINQ/EF aggregate methods invoked directly on the grouping, such as `g.Count()`, `g.Sum(...)`, `Enumerable.Count(g)`, or `Enumerable.Sum(g, ...)`.
