# LC022: ToList/ToArray Inside Select Projection

## What it flags

Flags per-row buffering inside projections because nested materialization often causes translation failures, client evaluation, or repeated in-memory work.

## Why it matters

LinqContraband reports this rule when the query shape suggests a risky or non-translatable pattern that is better made explicit before it reaches production.

## Typical fix

Keep the projection provider-friendly, flatten the shape, or materialize once at the outer boundary when nested collections are intentional.

The code fix is intentionally conservative. It only removes `ToList()` when the receiver type already matches the materialized type, such as a `List<T>` navigation projected as `navigation.ToList()`. It does not rewrite `ToArray()`, dictionary/set materializers, anonymous/object initializer members, or type-changing shapes such as `stringValue.ToList()`.

## Samples

See `samples/LinqContraband.Sample/Samples/LC022_ToListInSelectProjection/` for a focused example.

## The crime

```csharp
var query = db.Customers
    .Select(c => new
    {
        c.Id,
        OrderIds = c.Orders.Select(o => o.Id).ToList()
    });
```

## A better shape

```csharp
var query = db.Customers
    .Select(c => new
    {
        c.Id,
        OrderIds = c.Orders.Select(o => o.Id)
    });

var results = await query.ToListAsync();
```
