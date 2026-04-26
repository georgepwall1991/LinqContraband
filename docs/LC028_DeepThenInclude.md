# LC028: Deep ThenInclude Chain

## What it flags

Flags EF Core `ThenInclude(...)` chains once they exceed the configured maximum depth. By default, LC028 allows three consecutive `ThenInclude(...)` calls after an `Include(...)` and reports the fourth call as the point where the query should be reviewed.

## Why it matters

Very deep eager-loading chains often signal over-fetching, brittle query shapes, and poor separation between read models and entity graphs. They can also generate wide SQL with many joins and duplicated row data.

LC028 is intentionally heuristic: a deep Include graph can be valid for small reference data, aggregate snapshots, or carefully profiled queries. The diagnostic asks for a manual review rather than prescribing a universal rewrite.

## Safe and intentional shapes

LC028 does not report the first three `ThenInclude(...)` calls by default. If a project has a known, reviewed deep graph, either suppress the specific diagnostic with a justification or raise the threshold for that scope:

```editorconfig
[*.cs]
dotnet_code_quality.LC028.max_depth = 4
```

The threshold must be a positive integer. Invalid values keep the default of `3`.

## Typical fix

Prefer focused projections, split queries, or targeted follow-up loads for the specific data the caller actually consumes. There is no automatic fix because removing eager-loading depth requires a domain-specific decision about result shape, tracking behavior, and query ownership.

## Samples

See `samples/LinqContraband.Sample/Samples/LC028_DeepThenInclude/` for a focused example.

## The crime

```csharp
var query = db.Customers
    .Include(c => c.Orders)
        .ThenInclude(o => o.LineItems)
            .ThenInclude(li => li.Product)
                .ThenInclude(p => p.Supplier);
```

## A better shape

```csharp
var query = db.Customers
    .Select(c => new
    {
        c.Id,
        Orders = c.Orders.Select(o => new
        {
            o.Id,
            Items = o.LineItems.Select(li => new { li.Id, li.Product.Name })
        })
    });
```
