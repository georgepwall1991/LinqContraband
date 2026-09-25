---
layout: default
title: "LC057: Min, Max or Average throws on an empty query"
description: "LC057 flags EF Core Min, Max and Average over a non-nullable value, which throw 'Sequence contains no elements' when no row matches."
---

# LC057: Min, Max or Average throws on an empty query

## In Plain Terms

You asked for the most expensive item in an empty shop. There is no answer, and instead of saying "none" the cashier panics.

## Goal

Detect `Min`, `Max` and `Average` (and `MinAsync`, `MaxAsync`, `AverageAsync`) over a non-nullable value on an EF Core query, which throw when the query matches no rows.

## The Problem

SQL `MIN`, `MAX` and `AVG` return `NULL` for an empty set. When the result type is a non-nullable value type such as `decimal`, `int` or `DateTime`, EF Core has nowhere to put the `NULL` and throws `InvalidOperationException`: "Sequence contains no elements" (the in-memory provider reports "Nullable object must have a value"). The query works in development, where the table has data, and fails in production for the first customer, category or date range with no rows. `Sum` does not have this problem: EF Core returns `0`.

```csharp
// Violation: throws when the category has no products.
var highest = await db.Products
    .Where(p => p.CategoryId == categoryId)
    .MaxAsync(p => p.Price, ct);
```

See the EF Core issues [dotnet/efcore#18955](https://github.com/dotnet/efcore/issues/18955), [#20589](https://github.com/dotnet/efcore/issues/20589) and [#17988](https://github.com/dotnet/efcore/issues/17988).

## The Fix

Cast the selected value to its nullable type. The query then returns `null` for an empty set, and the code decides what that means:

```csharp
var highest = await db.Products
    .Where(p => p.CategoryId == categoryId)
    .MaxAsync(p => (decimal?)p.Price, ct) ?? 0m;
```

Checking `Any()` first also works, but costs a second round trip and can race with concurrent deletes.

## Analyzer Logic

### ID: `LC057`
### Category: `Reliability`
### Severity: `Warning`

1. Find `Queryable.Min`, `Queryable.Max` and `Queryable.Average`, and EF Core's `MinAsync`, `MaxAsync` and `AverageAsync`, with or without a selector.
2. Require the aggregated value, the selector's result or the element type of the selector-less overloads, to be a non-nullable value type.
3. Require the source to be an EF Core query: a `DbSet`, `DbContext.Set<T>()` or `Database.SqlQuery<T>()`, followed through LINQ and EF Core query operators, locals whose every assignment is such a query (including `q = q.Where(...)`), and non-overridable helper methods in the project whose every return is such a query, such as a repository's `IQueryable<Product> Query() => _db.Products;` or an `IQueryable<T>` extension that composes over its argument.

## When it stays quiet (non-goals)

- The value is already nullable: `Max(p => (decimal?)p.Price)`, `Max(p => p.Discount)` where `Discount` is `decimal?`, or a `Select` that projects a nullable value.
- The value is a reference type such as `string`: the result is `null` for an empty set.
- `Sum`, `Count` and other aggregates.
- The source is not provably EF Core: an `IQueryable<T>` parameter, field or property, an interface or virtual repository method, or an in-memory `AsQueryable()`. A query passed in as a parameter is often checked by the caller.
- The same method checks the query first: an earlier `Any`, `AnyAsync`, `Count`, `CountAsync`, `LongCount` or `LongCountAsync` call on a query that starts from the same local or expression, as in `if (!q.Any()) return;`, `q.Any() ? q.Max(...) : 0` or `var count = await q.CountAsync(); if (count == 0) return;`. The check is not inspected further: the rule prefers a missed report to a false one.
- Aggregates inside a `GroupBy` projection (`g.Max(...)`): a group always has at least one row.
- Aggregates inside another query's lambda, which EF Core translates as part of that query.
- LINQ to Objects aggregates over materialized lists.

## Code Fix

Casts the aggregated value to its nullable type: `Max(p => p.Price)` becomes `Max(p => (decimal?)p.Price)`, and a selector-less `Max()` gets a casting selector, `Max(x => (decimal?)x)`. When the result is used as the non-nullable value it was before, the fix appends `?? default` (in parentheses inside a larger expression) so the surrounding code keeps its type, and an empty query now yields `0`, `default(DateTime)` and so on instead of an exception. Replace `default` with the value that suits your code. When the result is already stored as the nullable type (`decimal? max = ...`), only the cast is added.

The fixer compiles the result and offers nothing when the rewrite would add an error or change the result's type: a selector that is not a lambda, an explicit generic argument such as `Max<Product, decimal>(...)`, or an async aggregate whose task is not awaited where it is created.

## Test Cases

### Violations

```csharp
db.Products.Max(p => p.Price);
await db.Products.Where(p => p.CategoryId == id).AverageAsync(p => p.Rating, ct);
db.Products.Select(p => p.Created).Min();
repository.Query().Max(p => p.Price);
```

### Valid

```csharp
db.Products.Max(p => (decimal?)p.Price);
db.Products.Sum(p => p.Price);
if (query.Any()) { var max = query.Max(p => p.Price); }
db.Products.GroupBy(p => p.CategoryId).Select(g => g.Max(p => p.Price));
```
