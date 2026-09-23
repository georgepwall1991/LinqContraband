---
layout: default
title: "LC051: ToAsyncEnumerable() runs an EF Core query synchronously"
description: "LC051 flags ToAsyncEnumerable() on EF Core queries, which enumerates them synchronously and blocks a thread per row, and switches to AsAsyncEnumerable()."
---

# LC051: ToAsyncEnumerable() runs an EF Core query synchronously

## In Plain Terms

You ask for a delivery service, but the courier walks every parcel over one at a time while you stand at the door. It looks asynchronous from the outside. Underneath, someone is waiting the whole time.

## Goal

Detect `ToAsyncEnumerable()` called directly on an EF Core query.

## The Problem

.NET 10 ships `System.Linq.AsyncEnumerable` in the box (earlier versions get it from the `System.Linq.Async` or `System.Linq.AsyncEnumerable` packages). Its `ToAsyncEnumerable()` accepts any `IEnumerable<T>`, and an EF Core query is one. So this compiles:

```csharp
// Violation: looks async, runs the query with synchronous I/O.
await foreach (var blog in db.Blogs.Where(b => b.Rating > 3).ToAsyncEnumerable())
{
    ...
}
```

`ToAsyncEnumerable()` only wraps the query's synchronous enumerator. EF Core opens the connection and reads every row synchronously, blocking a thread-pool thread for the whole query while the code looks asynchronous. Under load this starves the thread pool just like `.Result` on a task.

## The Fix

Use EF Core's `AsAsyncEnumerable()`, which returns the same `IAsyncEnumerable<T>` and streams rows with async I/O:

```csharp
await foreach (var blog in db.Blogs.Where(b => b.Rating > 3).AsAsyncEnumerable())
{
    ...
}
```

## Analyzer Logic

### ID: `LC051`
### Category: `Performance`
### Severity: `Warning`

1. Find `System.Linq.AsyncEnumerable.ToAsyncEnumerable(IEnumerable<T>)`, fluent or static.
2. Walk its source back through `Queryable` operators, EF Core query operators (`AsNoTracking`, `Include`, `TagWith`, ...), and query syntax.
3. Report when the walk reaches a `DbSet<T>` or `DbContext.Set<T>()`.

EF Core 11 ships its own check for this (EF1004), so LC051 stays quiet in projects that reference EF Core 11 or later.

## When it stays quiet (non-goals)

- In-memory sources: lists, arrays, and `AsQueryable()` over them. `AsAsyncEnumerable()` throws on a queryable EF Core does not back, so the rule only reports what it can prove is an EF query.
- Queries held in a variable, parameter, or returned by a helper. The rule cannot prove what backs them.
- Queries that already switched to LINQ to Objects with `AsEnumerable()`. That is an explicit choice to run the rest in memory.
- `ToAsyncEnumerable()` overloads for tasks and observables.

## Code Fix

The fix renames the call to `AsAsyncEnumerable()` and adds `using Microsoft.EntityFrameworkCore;` when the method is not already in scope. The static form (`AsyncEnumerable.ToAsyncEnumerable(query)`) is reported without a fix.

## Test Cases

### Violations

```csharp
db.Orders.ToAsyncEnumerable();
db.Orders.AsNoTracking().Where(o => o.Total > 0).Select(o => o.Id).ToAsyncEnumerable();
(from o in db.Orders where o.Total > 0 select o.Id).ToAsyncEnumerable();
```

### Valid

```csharp
db.Orders.Where(o => o.Total > 0).AsAsyncEnumerable();
cachedOrders.AsQueryable().Where(o => o.Total > 0).ToAsyncEnumerable();
db.Orders.AsEnumerable().Where(o => o.Total > 0).ToAsyncEnumerable();
```
