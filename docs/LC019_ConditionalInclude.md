# LC019: Conditional Include Expression

## What it flags

Flags conditional logic embedded in EF Core Include paths because include graphs must stay shape-stable to translate reliably and remain predictable. This includes root-level ternary/null-coalescing expressions and conditional receivers inside a longer navigation path.

## Why it matters

EF Core expects Include and ThenInclude lambdas to describe a concrete navigation path. Choosing between alternate navigations inside the lambda can fail at runtime or make the eager-loading shape ambiguous. Split the query shape before the Include call so each branch has a normal navigation path.

## What it does not flag

LC019 is limited to EF Core `Include` and `ThenInclude` calls. It does not report unrelated application extension methods named `Include`, and it does not treat conditionals inside filtered Include predicates as conditional navigation paths.

## Typical fix

Project a conditional shape outside the Include chain, split the query, or load alternate branches explicitly. This rule is manual-only because the correct replacement usually depends on whether both branches should run, whether the branches need different filters, and how the query is composed afterward.

## Samples

See `samples/LinqContraband.Sample/Samples/LC019_ConditionalInclude/` for a focused example.

## The crime

```csharp
var query = db.Orders
    .Include(o => includeCustomer ? o.Customer : o.AlternateCustomer);
```

```csharp
var query = db.Orders
    .Include(o => (includePrimary ? o.PrimaryCustomer : o.FallbackCustomer).Address);
```

## A better shape

```csharp
var query = includeCustomer
    ? db.Orders.Include(o => o.Customer)
    : db.Orders.Include(o => o.AlternateCustomer);
```

```csharp
var query = includePrimary
    ? db.Orders.Include(o => o.PrimaryCustomer.Address)
    : db.Orders.Include(o => o.FallbackCustomer.Address);
```
