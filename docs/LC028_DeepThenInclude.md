# LC028: Deep ThenInclude Chain

## What it flags

Flags very deep eager-loading chains because they often signal over-fetching, brittle query shapes, and poor separation between read models and entity graphs.

## Why it matters

LinqContraband reports this rule when the query shape suggests a risky or non-translatable pattern that is better made explicit before it reaches production.

## Typical fix

Prefer focused projections, split queries, or targeted follow-up loads for the specific data the caller actually consumes.

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
