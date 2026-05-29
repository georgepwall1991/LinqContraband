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

LC024 applies to fluent `GroupBy(...).Select(...)` and query-syntax `group ... by ... into g select ...` projections. It stays quiet for aggregate-only projections (`Key`, `Count`, `LongCount`, `Sum`, `Average`, `Min`, `Max`, `Any`, `All`) and for LINQ-to-Objects grouping where the source is already `IEnumerable<T>`.

### Translatable aggregate chains

EF Core 9 translates aggregates that filter or project the group before collapsing it to a scalar, so these stay quiet:

```csharp
g.Any()                                 // EXISTS / COUNT(*) > 0
g.Where(o => o.Amount > 0).Count()      // COUNT over a filtered set
g.Select(o => o.Amount).Sum()           // SUM(Amount) — same as g.Sum(o => o.Amount)
g.Distinct().Count()                    // COUNT(DISTINCT ...)
```

The exemption follows an aggregate whose receiver chain roots at the grouping parameter through translatable operators (`Where`, `Select`, `OrderBy`/`OrderByDescending`/`ThenBy`/`ThenByDescending`, `Distinct`), as well as the direct forms `g.Count()`, `g.Sum(...)`, `Enumerable.Count(g)`, `Enumerable.Sum(g, ...)`.

A chain that **terminates in a non-aggregate** is still reported, because it returns a sub-sequence or materializes per group rather than collapsing to a scalar: a bare `g.Where(p)`, a materializer `g.Select(s).ToList()`, and an element accessor `g.OrderBy(s).First()` all remain crimes.

The exemption is deliberately conservative about the chain's `Where`/`Select` lambda bodies: it covers only **invocation-free** predicates and selectors (member access, comparisons, arithmetic). Any method call inside a predicate or selector keeps the chain reported — a local function, a user-defined method (`g.Select(o => Scale(o.Amount)).Sum()`), or a non-translatable BCL overload (`o.Name.Equals(s, StringComparison.OrdinalIgnoreCase)`, `Regex.IsMatch(...)`) — because the rule does not assume translatability it cannot prove from the expression shape alone.
