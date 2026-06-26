---
layout: default
title: EF Core N+1 Query Detector
description: Use LinqContraband as an EF Core N+1 query detector that flags database calls inside loops, missing includes, and loading patterns before production.
permalink: /ef-core-n-plus-one-query-detector/
body_class: page-n-plus-one-detector
---

# EF Core N+1 Query Detector

LinqContraband is a compile-time EF Core N+1 query detector for .NET projects. It uses Roslyn analyzers to flag risky
query and loading patterns while you are still in the IDE or CI build, before the code becomes a slow endpoint.

Install the official NuGet package:

```bash
dotnet add package LinqContraband
```

## What N+1 Looks Like in EF Core

The classic N+1 shape is a query that loads a set of entities, then performs more database work once per row:

```csharp
var users = await db.Users.ToListAsync();

foreach (var user in users)
{
    var orders = await db.Orders.Where(order => order.UserId == user.Id).ToListAsync();
    Console.WriteLine($"{user.Name}: {orders.Count}");
}
```

This can turn one request into dozens, hundreds, or thousands of database roundtrips. It often looks harmless in code
review because each individual query is small.

## LinqContraband Rules That Help

| Rule | What it detects | Why it matters |
| --- | --- | --- |
| [LC007: database execution inside loop](/LinqContraband/LC007_NPlusOneLooper.html) | EF Core query execution from `for`, `foreach`, `while`, and related loop bodies. | Prevents repeated database calls from scaling with row count. |
| [LC045: missing include](/LinqContraband/LC045_MissingInclude.html) | Navigation access after materialization when the query did not include the navigation. | Catches missing eager loading before it becomes lazy-loading churn or null/empty navigation data. |
| [LC006: multiple collection includes](/LinqContraband/LC006_CartesianExplosion.html) | Sibling collection includes that may cause cartesian explosion. | Helps balance the "fix N+1 with Include" path against over-eager loading. |
| [LC038: excessive eager loading](/LinqContraband/LC038_ExcessiveEagerLoading.html) | Queries that include more navigations than the configured threshold. | Keeps eager loading intentional instead of turning every query into a wide graph fetch. |
| [LC042: missing query tags](/LinqContraband/LC042_MissingQueryTags.html) | Complex query shapes without `TagWith`. | Makes expensive queries easier to identify in database telemetry. |

## Safer Fix Patterns

Prefer set-based queries, projection, and explicit loading boundaries:

```csharp
var userSummaries = await db.Users
    .Select(user => new
    {
        user.Name,
        OrderCount = user.Orders.Count
    })
    .ToListAsync();
```

When you genuinely need related entities, make the loading strategy explicit:

```csharp
var users = await db.Users
    .Include(user => user.Orders)
    .AsSplitQuery()
    .ToListAsync();
```

LinqContraband does not blindly demand `Include()` everywhere. It pairs N+1 detection with rules for cartesian
explosion, deep include chains, excessive eager loading, and whole-entity projection so the fix remains appropriate for
the query.

## Why Compile-Time Detection Helps

N+1 queries are easy to miss in unit tests because small test datasets hide the cost. A compile-time analyzer gives the
team feedback at the pull-request stage, before the query has production traffic and real row counts behind it.

LinqContraband runs as an analyzer package only. It adds no runtime dependency to your application and works with
Visual Studio, Rider, VS Code, and CI builds.

## Official Links

- Canonical repository: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Official NuGet package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Full rule catalog: [georgepwall1991.github.io/LinqContraband/rule-catalog.html](https://georgepwall1991.github.io/LinqContraband/rule-catalog.html)
- Safe install guidance: [Official LinqContraband downloads and authenticity](/LinqContraband/security-and-authenticity/)
