---
layout: default
title: "LC022: Nested Collection Materialization Inside Projection"
---

# LC022: Nested Collection Materialization Inside Projection

## In Plain Terms

Imagine you ask a baker to put frosting on 100 cupcakes. But for each
cupcake, you tell them "First, put all the frosting in a separate bowl, then frost the cupcake from the bowl." The baker
gets frustrated because they could just frost the cupcake directly — the extra bowl step is pointless and slows
everything down.

## What it flags

Flags nested collection materialization inside projections because it can be expensive, provider-version sensitive, or better expressed with direct projection/split-query shaping.

## Why it matters

LinqContraband reports this rule as an advisory performance signal. Modern EF Core can translate some correlated collection projections, so the diagnostic should prompt review rather than automatic removal.

## Typical fix

Keep the projection provider-friendly, flatten the shape, use split queries where appropriate, or keep the nested materializer when a DTO contract requires a concrete collection.

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
