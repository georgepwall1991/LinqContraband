# LC019: Conditional Include Expression

## What it flags

Flags conditional logic embedded inside Include paths because EF Core include graphs must stay shape-stable to translate reliably and remain predictable.

## Why it matters

LinqContraband reports this rule when the query shape suggests a risky or non-translatable pattern that is better made explicit before it reaches production.

## Typical fix

Project a conditional shape outside the Include chain, split the query, or load alternate branches explicitly.

## Samples

See `samples/LinqContraband.Sample/Samples/LC019_ConditionalInclude/` for a focused example.

## The crime

```csharp
var query = db.Orders
    .Include(o => includeCustomer ? o.Customer : o.AlternateCustomer);
```

## A better shape

```csharp
var query = includeCustomer
    ? db.Orders.Include(o => o.Customer)
    : db.Orders.Include(o => o.AlternateCustomer);
```
