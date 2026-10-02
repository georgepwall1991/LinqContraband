---
layout: default
title: "LC022: Nested Collection Materialization Inside Projection"
description: "LC022 flags ToDictionary on a nested collection in an EF Core Select projection, which throws, and nested ToList on EF Core 7 or older."
---

# LC022: Nested Collection Materialization Inside Projection

## In Plain Terms

You ask a librarian for every author together with their books, filed by ISBN. The librarian can hand you each author
with a stack of books, but cannot hand you a ready-made index: you have to build the index yourself once you have the
books. Asking EF Core for `ToDictionary` inside the query is asking for that index, and EF Core refuses.

## What it flags

A collection materializer called on a nested collection inside a `Select` projection over an EF Core query:

- `ToDictionary` (and `ToDictionaryAsync`) on every EF Core version. From EF Core 3.0 on, EF Core cannot translate it
  and throws "The LINQ expression ... could not be translated" when the query runs, and the message says so. On EF Core
  2.x, which evaluates such fragments on the client, or in a project without EF Core, it is an advisory.
- `ToListAsync`, `ToArrayAsync` and `ToHashSetAsync` on every EF Core version, as an advisory. They return tasks, and
  EF Core does not strip them from a projection.
- `ToList`, `ToArray` and `ToHashSet` only when the project references EF Core 7 or older, or when the EF Core version
  or a relational provider cannot be found, as an advisory query-shape review.

## Why it matters

EF Core 8 and later with a relational provider translate a correlated collection projection to the same SQL whether or
not it ends in `ToList()`, `ToArray()` or `ToHashSet()`. Probed with SQLite on EF Core 8.0.20 and 10.0.0,
`c.Orders.Select(o => o.Id).ToList()` and `c.Orders.Select(o => o.Id)` both produce one `LEFT JOIN` ordered by the
customer key, and `c.Orders.ToList().Count` becomes a `COUNT(*)` subquery. Those calls cost nothing there, and a DTO
that needs a `List<T>` should keep them, so LC022 stays quiet on them.

`ToDictionary` is different: no EF Core version translates it on a nested collection, so from EF Core 3.0 on the query
fails at run time.

If a query projects several collections, the cost to look at is the row explosion of the joins, which LC006 covers;
removing `ToList()` does not change it.

## Typical fix

For `ToDictionary`, project the nested rows (as a list or an anonymous shape) and build the dictionary after the query
has run. For the advisory report on older EF Core, project the collection directly, use split queries where
appropriate, or keep the materializer when a DTO contract requires a concrete collection.

LC022 does not report a projection over an `IQueryable` that provably wraps an in-memory collection (`list.AsQueryable()`
or `new EnumerableQuery<T>(...)`), because nothing is sent to a database there. `AsQueryable()` over an `IEnumerable<T>`
or an `IQueryable` parameter still reports. It also stays quiet inside a `GroupBy` projection, which LC024 covers.

The code fix is intentionally conservative. It only removes `ToList()` when the receiver type already matches the
materialized type, such as a `List<T>` navigation projected as `navigation.ToList()`, so it only appears on the
advisory report. It does not rewrite `ToArray()`, dictionary or set materializers, anonymous or object initializer
members, or type-changing shapes such as `stringValue.ToList()`.

## Samples

See `samples/LinqContraband.Sample/Samples/LC022_ToListInSelectProjection/` for a focused example.

## The crime

```csharp
var customers = await db.Customers
    .Select(c => new
    {
        c.Id,
        OrdersById = c.Orders.ToDictionary(o => o.Id) // throws: could not be translated
    })
    .ToListAsync();
```

## A better shape

```csharp
var rows = await db.Customers
    .Select(c => new { c.Id, Orders = c.Orders.ToList() })
    .ToListAsync();

var customers = rows
    .Select(c => new { c.Id, OrdersById = c.Orders.ToDictionary(o => o.Id) })
    .ToList();
```
