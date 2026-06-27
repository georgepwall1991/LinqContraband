---
layout: default
title: EF Core Include Analyzer
description: Use LinqContraband as an EF Core Include analyzer for missing includes, N+1 loading, cartesian explosion, deep ThenInclude chains, excessive eager loading, and query tags.
permalink: /ef-core-include-analyzer/
body_class: page-include-analyzer
---

# EF Core Include Analyzer

LinqContraband is an EF Core `Include` analyzer for .NET projects that want loading problems to appear during
development instead of after a slow endpoint reaches production. It catches missing related data, lazy-loading churn,
cartesian explosion risks, and over-eager graphs at compile time.

Install the official analyzer package:

```bash
dotnet add package LinqContraband
```

## Why Include Needs Review

`Include()` can fix a missing navigation, but it can also create a much larger query than the caller needs. A useful
review has to answer both questions:

- Did the query load the related data that the code reads after materialization?
- Did the fix keep the eager-loading graph bounded enough for production row counts?

The common failure shape is a materialized entity followed by navigation access:

```csharp
var orders = db.Orders.ToList();

foreach (var order in orders)
{
    Console.WriteLine(order.Customer.Name);
}
```

With lazy loading enabled, that can become an extra database query per row. Without lazy loading, the navigation may be
null or empty. Adding `Include()` can be right, but a projection may be cheaper when the caller only needs a few fields.

## LinqContraband Rules That Help

| Rule | What it detects | Review direction |
| --- | --- | --- |
| [LC045: missing include](/LinqContraband/LC045_MissingInclude.html) | Navigation access after materialization when the query did not include the navigation. | Add the missing `Include`/`ThenInclude` path or project the needed values in SQL. |
| [LC006: cartesian explosion risk](/LinqContraband/LC006_CartesianExplosion.html) | Sibling collection includes without an effective `AsSplitQuery()`. | Use `AsSplitQuery()` when the split-query trade-off is acceptable, or keep a reviewed single query with justification. |
| [LC019: conditional include expression](/LinqContraband/LC019_ConditionalInclude.html) | Conditional navigation choices inside `Include` or `ThenInclude` paths. | Split the query branch before `Include`, eager-load both explicit paths, or project a conditional result shape. |
| [LC028: deep ThenInclude chain](/LinqContraband/LC028_DeepThenInclude.html) | `ThenInclude` chains deeper than the configured review threshold. | Prefer focused projections, split queries, or a documented threshold/suppression for known aggregate loads. |
| [LC038: excessive eager loading](/LinqContraband/LC038_ExcessiveEagerLoading.html) | Too many `Include`/`ThenInclude` calls on one provable EF query root. | Check whether every navigation is used and whether unrelated branches should be loaded separately. |
| [LC042: missing query tags](/LinqContraband/LC042_MissingQueryTags.html) | Complex EF query shapes without `TagWith(...)` or `TagWithCallSite()`. | Add a stable tag so expensive Include graphs can be found in logs, profilers, and query plans. |

## Safer Loading Patterns

Project when the caller only needs a read model:

```csharp
var rows = await db.Orders
    .Select(order => new OrderRow
    {
        Id = order.Id,
        CustomerName = order.Customer.Name,
        OpenLineCount = order.Lines.Count(line => line.IsOpen)
    })
    .ToListAsync();
```

Use explicit eager loading when the caller genuinely needs the entity graph:

```csharp
var orders = await db.Orders
    .Include(order => order.Customer)
    .Include(order => order.Lines)
    .AsSplitQuery()
    .TagWith("Orders screen: customer and line graph")
    .ToListAsync();
```

Split conditional loading before the Include chain:

```csharp
var query = includeBilling
    ? db.Orders.Include(order => order.Customer.BillingAddress)
    : db.Orders.Include(order => order.Customer.ShippingAddress);
```

Do not treat `Include()` as the default fix for every navigation warning. For list screens, API responses, and
dashboards, a projection often gives EF Core a smaller SQL shape and avoids tracking a graph that the caller never
edits.

## CI Severity Starter

Start with the correctness and production-cost rules, then tune the advisory thresholds to the project:

```ini
[*.cs]

# Missing related data and hidden N+1 loading
dotnet_diagnostic.LC045.severity = warning

# Eager-loading cost review
dotnet_diagnostic.LC006.severity = warning
dotnet_diagnostic.LC019.severity = warning
dotnet_diagnostic.LC028.severity = suggestion
dotnet_diagnostic.LC038.severity = suggestion
dotnet_diagnostic.LC042.severity = suggestion
```

Use the [EF Core query analyzer CI guide](/LinqContraband/ef-core-query-analyzer-ci/) when these diagnostics should run
on every pull request. Use the [EF Core N+1 query detector guide](/LinqContraband/ef-core-n-plus-one-query-detector/)
when loop execution and missing related data are the main concern.

## Official Links

- Canonical repository: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Official NuGet package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Full rule catalog: [georgepwall1991.github.io/LinqContraband/rule-catalog.html](https://georgepwall1991.github.io/LinqContraband/rule-catalog.html)
- Safe install guidance: [Official LinqContraband downloads and authenticity](/LinqContraband/security-and-authenticity/)
