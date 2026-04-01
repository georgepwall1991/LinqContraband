# LC024: GroupBy with Non-Translatable Projection

## What it flags

Flags GroupBy pipelines that project values EF Core cannot translate cleanly to SQL, which usually triggers runtime failures or accidental client-side grouping.

## Why it matters

LinqContraband reports this rule when the query shape suggests a risky or non-translatable pattern that is better made explicit before it reaches production.

## Typical fix

Move the projection before the grouping only when it stays translatable, aggregate on the server, or materialize intentionally before performing complex grouping in memory.

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
