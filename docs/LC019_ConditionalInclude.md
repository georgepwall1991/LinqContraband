---
layout: default
title: "LC019: Conditional Include Expression"
---

# LC019: Conditional Include Expression

## What it flags

Flags conditional logic embedded in EF Core Include paths because include graphs must stay shape-stable to translate reliably and remain predictable. This includes root-level ternary/null-coalescing expressions and conditional receivers inside a longer navigation path.

## Why it matters

EF Core expects `Include` and `ThenInclude` lambdas to describe a concrete navigation path. Choosing between alternate navigations inside the lambda can fail at runtime or make the eager-loading shape ambiguous. Split the query shape before the `Include` call so each branch has a normal navigation path.

The problem is not the `?:` operator by itself. A conditional inside a filtered Include predicate, ordering selector, or `Take` argument does not choose between navigation paths, so LC019 leaves those shapes alone.

## What it does not flag

LC019 is limited to EF Core `Include` and `ThenInclude` calls. It does not report unrelated application extension methods named `Include` or `ThenInclude`, and it does not treat conditionals inside filtered Include operators as conditional navigation paths.

## Typical fix

Project a conditional shape outside the Include chain, split the query, or load alternate branches explicitly. This rule is manual-only because the correct replacement usually depends on whether both branches should run, whether the branches need different filters, and how the query is composed afterward.

Use this decision path:

1. If exactly one navigation should be eager-loaded, split the query before `Include`.
2. If both navigations are useful to the caller, eager-load both explicit paths.
3. If the caller only needs scalar values or a small DTO, project the conditional shape with `Select` instead of using `Include`.
4. If each branch needs a different filtered Include, keep the filters inside two separate branch-specific Include chains.

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

```csharp
var query = db.Orders
    .Include(o => o.Customer)
    .ThenInclude(c => (preferBilling ? c.BillingAddress : c.ShippingAddress).Country);
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

```csharp
var query = preferBilling
    ? db.Orders.Include(o => o.Customer).ThenInclude(c => c.BillingAddress.Country)
    : db.Orders.Include(o => o.Customer).ThenInclude(c => c.ShippingAddress.Country);
```

## When projection is better

If the conditional is there because the result model needs "the active address" or "the preferred contact", a projection is often clearer than an Include:

```csharp
var query = db.Orders.Select(o => new OrderSummary
{
    Id = o.Id,
    Address = preferBilling ? o.Customer.BillingAddress : o.Customer.ShippingAddress
});
```

Projection keeps the conditional in the result shape, where EF can translate ordinary scalar/entity selection, instead of trying to make the eager-loading graph dynamic.

## When eager-loading both branches is better

If the caller will inspect both possible navigations after materialization, include both paths explicitly:

```csharp
var query = db.Orders
    .Include(o => o.PrimaryCustomer.Address)
    .Include(o => o.FallbackCustomer.Address);
```

This may load more data, so prefer it only when both branches are genuinely needed. If only one branch is used per request, split before Include instead.

## Filtered Include boundary

Conditionals inside filtered Include operators are allowed because they do not change the navigation path:

```csharp
var query = db.Orders.Include(o => o.Lines
    .Where(l => priorityOnly ? l.IsPriority : l.IsBackordered)
    .OrderBy(l => newestFirst ? l.CreatedAt : l.Id)
    .Take(shortList ? 5 : 10));
```

The navigation path remains `Orders -> Lines`; only the filter/order/window changes.
