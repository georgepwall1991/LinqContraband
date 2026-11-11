---
layout: default
title: EF Core N+1 Query Detector
description: Use LinqContraband as an EF Core N+1 query detector for database calls in loops, lambdas and repository calls, missing includes and saves per item.
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

The same scaling problem can happen on the write side. For repeated commits and N+1 writes, see the
[EF Core SaveChanges in loop analyzer guide](/LinqContraband/ef-core-savechanges-in-loop-analyzer/).

## LinqContraband Rules That Help

| Rule | What it detects | Why it matters |
| --- | --- | --- |
| [LC007: database execution inside loop](/LinqContraband/LC007_NPlusOneLooper.html) | EF Core query execution from `for`, `foreach`, `while`, and related loop bodies. | Prevents repeated database calls from scaling with row count. |
| [LC010: SaveChanges inside loop](/LinqContraband/LC010_SaveChangesInLoop.html) | `SaveChanges` and `SaveChangesAsync` called once per item, directly or through a repository or unit of work. | Prevents one transaction and one round trip per row on the write side. |
| [LC045: missing include](/LinqContraband/LC045_MissingInclude.html) | Navigation access after materialization when the query did not include the navigation. | Catches missing eager loading before it becomes lazy-loading churn or null/empty navigation data. |
| [LC006: multiple collection includes](/LinqContraband/LC006_CartesianExplosion.html) | Sibling collection includes that may cause cartesian explosion. | Helps balance the "fix N+1 with Include" path against over-eager loading. |
| [LC038: excessive eager loading](/LinqContraband/LC038_ExcessiveEagerLoading.html) | Queries that include more navigations than the configured threshold. | Keeps eager loading intentional instead of turning every query into a wide graph fetch. |
| [LC042: missing query tags](/LinqContraband/LC042_MissingQueryTags.html) | Complex query shapes without `TagWith`. | Makes expensive queries easier to identify in database telemetry. |

## N+1 Across Methods and Layers

Outside a tutorial, the loop and the query are rarely in the same method. The loop sits in a handler or a service, and
the query sits in a repository behind an interface:

```csharp
public List<OrderDto> Handle(IReadOnlyList<Order> orders)
{
    var result = new List<OrderDto>();
    foreach (var order in orders)
    {
        var customer = _customers.GetById(order.CustomerId); // LC007: one query per order
        result.Add(new OrderDto(order.Id, customer.Name));
    }

    return result;
}

public sealed class CustomerRepository : ICustomerRepository
{
    public Customer GetById(int id) => _db.Customers.Single(customer => customer.Id == id);
}
```

LinqContraband follows the call from the loop to the query when it can read both ends:

- **Lambdas that run once per element.** `ids.Select(id => db.Customers.Find(id))`, `orders.ForEach(...)`,
  `Parallel.ForEach(...)` and `Task.WhenAll(ids.Select(...))` are loops written as expressions.
- **Helper methods in the same project**, up to three calls deep, including a method passed as a method group
  (`ids.Select(LoadCustomer)`).
- **Interfaces and virtual methods**, when every implementation in the project runs the query. If one of them is an
  in-memory or caching implementation, the call is ambiguous and stays quiet.
- **Methods that return a query.** `repository.Query().Count(order => order.CustomerId == id)` in a loop is reported
  when `Query()` returns an EF Core query.
- **Saves.** A repository whose `Add` calls `SaveChanges`, or a unit of work committed for every item, is reported by
  LC010 on the call in the loop.
- **Lazy loads.** LC045 reports a navigation read per entity in a loop, in a per-element lambda, in a private mapper
  method and in a computed property the entity declares.

A build compiles one project at a time, so the analyzer package cannot read a repository that lives in another project.
[`LinqContraband.Scan`](/LinqContraband/ef-core-query-scanner/) builds the whole solution and follows a per-item call
from one project into the next.

## Loops That Are Not N+1

A database call in a loop is not always one call per row. These shapes stay quiet, because they cost one round trip per
batch or attempt, or a number of round trips the source code fixes:

- Keyset and paged loops that stop when a page comes back short or empty, and drain loops such as
  `while (await query.Take(1000).ExecuteDeleteAsync() > 0)`.
- Polling loops in background services, and retry loops around a `try`/`catch`.
- `ids.Chunk(500)` batches, which run one query per batch.
- A save every N items: `if (++pending % 1000 == 0) db.SaveChanges();`.
- A loop over a list fixed in the source, such as an inline array or the values of an enum.
- A loop that leaves on its first pass.
- For LC045, a reference navigation to a table that `db.Customers.Load()` loaded whole before the query.

Each rule page lists the exact conditions.

## Measured Against a Real Database

The repository carries a benchmark for these rules, so that what they catch is a measurement and not a claim. Each case
is a small program that runs against SQLite at two data sizes while an interceptor counts database round trips. A case
counts as an N+1 only if the larger database cost it more round trips, and as safe only if the count stayed the same.
LC007, LC010 and LC045 then analyze the same source files with the package's default configuration.

<!-- n-plus-one-benchmark:start (generated by the benchmark's --write; do not edit by hand) -->

The benchmark has 154 cases: 105 proven N+1 and 49 proven safe.

| Measure | Cases | Reported | Rate |
| --- | ---: | ---: | ---: |
| Proven N+1 cases that are reported (recall) | 105 | 88 | 83.8% |
| Proven safe cases that are reported (false positives) | 49 | 3 | 6.1% |

Recall depends on what sits between the loop and the database call:

| How the loop reaches the database | Proven N+1 | Reported | Recall |
| --- | ---: | ---: | ---: |
| Written in the loop body | 38 | 36 | 94.7% |
| In a lambda that runs once per element | 20 | 20 | 100.0% |
| Through a helper method in the same project | 13 | 12 | 92.3% |
| Through a method or parameter that hands over an `IQueryable` | 6 | 5 | 83.3% |
| Through an interface | 9 | 8 | 88.9% |
| Through a virtual or abstract method | 3 | 3 | 100.0% |
| Through a delegate | 3 | 0 | 0.0% |
| Through a mediator | 3 | 0 | 0.0% |
| On entities that come from, or go to, another method | 5 | 4 | 80.0% |
| Through another project (in a build) | 5 | 0 | 0.0% |

<!-- n-plus-one-benchmark:end -->

How to read the numbers:

- The rules do not follow a delegate held in a variable or parameter, a mediator's `Send`, or, in a build, a call into
  another project. The scanner covers most of the cross-project cases.
- The safe cases are the loops a pattern matcher gets wrong: batching, paging, retries, memoized lookups. The few that
  are reported are reported on purpose, for example a `Find` per item that happens to hit entities the context already
  tracks.
- The cases were written for this repository from code seen in real applications. The numbers show what the rules can
  and cannot follow. They are not comparable with another tool's benchmark, and they do not predict the share of N+1
  code found in your codebase.

The benchmark runs in CI, and a change that gains or loses a case fails the build until the results are regenerated.
See [how the benchmark works](https://github.com/georgepwall1991/LinqContraband/blob/master/tools/NPlusOneBenchmark/README.md)
and [every case with its round-trip counts](https://github.com/georgepwall1991/LinqContraband/blob/master/tools/NPlusOneBenchmark/results.md).

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
the query. For the loading-specific rule set, see the
[EF Core Include analyzer guide](/LinqContraband/ef-core-include-analyzer/).

## Why Compile-Time Detection Helps

N+1 queries are easy to miss in unit tests because small test datasets hide the cost. A compile-time analyzer gives the
team feedback at the pull-request stage, before the query has production traffic and real row counts behind it.

LinqContraband runs as an analyzer package only. It adds no runtime dependency to your application and works with
Visual Studio, Rider, VS Code, and CI builds.

To make N+1 diagnostics visible on every pull request, see the
[EF Core query analyzer CI guide](/LinqContraband/ef-core-query-analyzer-ci/).

## Official Links

- Canonical repository: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Official NuGet package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Full rule catalog: [georgepwall1991.github.io/LinqContraband/rule-catalog.html](https://georgepwall1991.github.io/LinqContraband/rule-catalog.html)
- Safe install guidance: [Official LinqContraband downloads and authenticity](/LinqContraband/security-and-authenticity/)
