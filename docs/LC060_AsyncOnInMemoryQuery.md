---
layout: default
title: "LC060: EF Core async operator on an in-memory query"
description: "LC060 flags EF Core ToListAsync, CountAsync and other async operators on AsQueryable() over an in-memory collection, which throw at run time."
---

# LC060: EF Core async operator on an in-memory query

## In Plain Terms

You rang the kitchen to ask for a sandwich that is already on your plate. Nobody answers that phone, so you go hungry.

## Goal

Detect EF Core async query operators (`ToListAsync`, `FirstOrDefaultAsync`, `CountAsync`, `AnyAsync`, `AsAsyncEnumerable`, ...) called on a query that comes from `AsQueryable()` over a list, array or other in-memory collection.

## The Problem

`Queryable.AsQueryable()` over a `List<T>` or an array returns an `EnumerableQuery<T>`: a LINQ to Objects query dressed as an `IQueryable<T>`. It does not implement `IAsyncEnumerable<T>`, and its provider is not EF Core's `IAsyncQueryProvider`. Every async operator in `EntityFrameworkQueryableExtensions` needs one of the two, so each call throws:

```
System.InvalidOperationException: The source 'IQueryable' doesn't implement 'IAsyncEnumerable<Item>'.
Only sources that implement 'IAsyncEnumerable' can be used for Entity Framework asynchronous operations.
```

The code compiles, because `EnumerableQuery<T>` is an `IQueryable<T>`, and it fails on every run. It usually appears when a method that used to query a `DbSet` is changed to filter a list it already loaded, or in in-memory repositories and test fakes (see [dotnet/efcore#35666](https://github.com/dotnet/efcore/issues/35666)).

```csharp
// Violation: throws InvalidOperationException every time.
var items = await list.AsQueryable().Where(x => x.Active).ToListAsync(ct);
```

## The Fix

The data is already in memory, so there is no I/O to await. Use the synchronous operator:

```csharp
var items = list.AsQueryable().Where(x => x.Active).ToList();
// or simply
var items = list.Where(x => x.Active).ToList();
```

If the query is meant to run against the database, build it from the `DbSet`. Test code that needs an async-capable fake can use a library such as MockQueryable (`list.BuildMock()`), whose queryable implements `IAsyncEnumerable<T>`.

## Analyzer Logic

### ID: `LC060`
### Category: `Reliability`
### Severity: `Warning`

Reports a call to an `EntityFrameworkQueryableExtensions` method whose name ends in `Async`, or to `AsAsyncEnumerable`, when its source is proven to be an `EnumerableQuery<T>`:

1. The chain starts at `Queryable.AsQueryable()` over an array, or over a class or struct that is neither queryable nor asynchronously enumerable (`List<T>`, `HashSet<T>`, ...), directly or through a cast.
2. Or it starts at `AsQueryable()` over the result of a LINQ to Objects operator (`Where`, `Select`, `OrderBy`, `Skip`, `Take`, `Concat`, ...), which never returns a queryable.
3. Or it starts at `new EnumerableQuery<T>(...)`.
4. Between the root and the async call there are only `Queryable` operators (`Where`, `Select`, `OrderBy`, `Skip`, `Take`, ...), `AsQueryable()` on a query, and EF Core's non-executing operators (`AsNoTracking`, `Include`, `TagWith`, ...), which return a query that EF Core does not run unchanged.
5. A local counts when every write to it in the method is such a chain, or a composition of the same local (`q = q.Where(...)`).
6. A call to a non-overridable helper method in the same project counts when every `return` in it is such a chain, or composes one `IQueryable` parameter that the call passes such a chain.

## When it stays quiet (non-goals)

- Queries from a `DbSet`, `Set<T>()`, or any other source EF Core runs.
- `AsQueryable()` over something that is already queryable, such as `db.Items.AsQueryable()`. It is a cast, and the query stays an EF Core query.
- Parameters, fields and properties of type `IQueryable<T>`, virtual or interface helpers, and locals that any path assigns from one of those. Their provenance is unknown.
- `AsQueryable()` over an interface-typed sequence (`IEnumerable<T>`, `IList<T>`) that is a parameter, field or local, because it may be a `DbSet` at run time. `AsEnumerable()`, `Cast()` and `OfType()` can hand back their source, so they do not prove an in-memory source either.
- Sources that can be enumerated asynchronously: MockQueryable's `BuildMock()`, and collections that implement `IAsyncEnumerable<T>`.
- Synchronous operators on an in-memory query, and EF Core methods that do not run the query, such as `ToQueryString()` and `AsNoTracking()`.
- A `ToListAsync` from another library that handles in-memory queries itself.

## Code Fix

Replaces the awaited async call with its synchronous LINQ twin, dropping the `await`, a `ConfigureAwait(...)` and the cancellation token:

- `await q.ToListAsync(ct)` becomes `q.ToList()`.
- `await q.FirstOrDefaultAsync(x => x.Active, ct).ConfigureAwait(false)` becomes `q.FirstOrDefault(x => x.Active)`.
- The same for `ToArrayAsync`, `ToHashSetAsync`, `ToDictionaryAsync`, `FirstAsync`, `SingleAsync`, `SingleOrDefaultAsync`, `LastAsync`, `LastOrDefaultAsync`, `ElementAtAsync`, `CountAsync`, `LongCountAsync`, `AnyAsync`, `AllAsync`, `ContainsAsync`, `SumAsync`, `MinAsync`, `MaxAsync` and `AverageAsync`.

No fix is offered for `ForEachAsync`, `LoadAsync` and `AsAsyncEnumerable`, for a call that is not awaited where it is made (a task stored in a local or passed to `Task.WhenAll`), for the static call form `EntityFrameworkQueryableExtensions.ToListAsync(q)`, or for named arguments. The fixer compiles the result and offers nothing when the rewrite adds an error, or when it would leave an async method without an `await` (CS1998), which some builds treat as an error. Fix All applies each fix on its own, so fixing every call in a method that only awaits those calls can still leave CS1998 for you to resolve.

## Test Cases

### Violations

```csharp
var items = await list.AsQueryable().Where(x => x.Active).ToListAsync(ct);
var count = await array.AsQueryable().CountAsync(ct);
var q = list.AsQueryable(); q = q.OrderBy(x => x.Id); var first = await q.FirstOrDefaultAsync(ct);
await foreach (var item in list.AsQueryable().AsAsyncEnumerable()) { }
var items = await new HashSet<Item>().AsQueryable().ToListAsync(ct);
var items = await list.SelectMany(_ => array).AsQueryable().ToListAsync(ct);
var items = await list.Concat(array).AsQueryable().ToListAsync(ct);
var items = await list.OrderBy(x => x.Id).ThenBy(x => x.Price).AsQueryable().ToListAsync(ct);
var items = await list.Distinct().AsQueryable().ToListAsync(ct);
```

### Valid

```csharp
var items = await db.Items.Where(x => x.Active).ToListAsync(ct);
var items = await db.Items.AsQueryable().ToListAsync(ct);
var items = await query.ToListAsync(ct);            // IQueryable<T> parameter
var items = await list.BuildMock().ToListAsync(ct); // MockQueryable
var items = list.AsQueryable().Where(x => x.Active).ToList();
IList<Item> items = list; await items.AsQueryable().ToListAsync(ct);
ICollection<Item> items = list; await items.AsQueryable().ToListAsync(ct);
ISet<Item> items = new HashSet<Item>(); await items.AsQueryable().ToListAsync(ct);
await sequence.OfType<Item>().AsQueryable().ToListAsync(ct);
```
