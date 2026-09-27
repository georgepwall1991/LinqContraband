---
layout: default
title: "LC061: Compiled query is not cached"
description: "LC061 flags EF Core EF.CompileQuery and EF.CompileAsyncQuery calls that compile the query on every call instead of once in a static readonly field."
---

# LC061: Compiled query is not cached

## In Plain Terms

You had a key cut so you would not have to pick the lock every morning, then threw the key away each night and had a new one cut the next day.

## Goal

Detect `EF.CompileQuery` and `EF.CompileAsyncQuery` calls whose delegate is created again every time the code runs, instead of once.

## The Problem

A compiled query saves EF Core the work of looking the query up in its query cache, but only when the delegate is created once and reused. That is why the [EF Core documentation on compiled queries](https://learn.microsoft.com/ef/core/performance/advanced-performance-topics#compiled-queries) stores it in a `private static readonly` field. Calling `EF.CompileQuery` inside a method builds the expression tree and compiles the query on every call. That costs more than the ordinary LINQ query it replaced, which at least finds its compiled form in EF Core's query cache after the first run.

```csharp
// Violation: compiles the query on every call.
public Task<Blog?> Get(int id) =>
    EF.CompileAsyncQuery((BlogContext c, int i) => c.Blogs.FirstOrDefault(b => b.Id == i))(_db, id);
```

## The Fix

Compile once, into a static readonly field, and invoke the field:

```csharp
private static readonly Func<BlogContext, int, Task<Blog?>> GetQuery =
    EF.CompileAsyncQuery((BlogContext c, int i) => c.Blogs.FirstOrDefault(b => b.Id == i));

public Task<Blog?> Get(int id) => GetQuery(_db, id);
```

## Analyzer Logic

### ID: `LC061`
### Category: `Performance`
### Severity: `Warning`

Reports `Microsoft.EntityFrameworkCore.EF.CompileQuery` and `EF.CompileAsyncQuery` when the delegate is:

1. Invoked straight away, in a method, instance constructor, instance field or property initializer, accessor, lambda or local function: `EF.CompileQuery(...)(db, id)` or `EF.CompileQuery(...).Invoke(db, id)`. This includes a delegate nested inside a cache factory, such as `cache.GetOrAdd(key, _ => (c, id) => EF.CompileQuery(...)(c, id))`: the cache keeps the inner delegate, which compiles on every call.
2. Assigned to a local whose every use invokes it: `var query = EF.CompileQuery(...); return query(db, id);`.
3. Returned from an expression-bodied property or a `get` accessor, which compiles it on every read: `static Func<...> ById => EF.CompileQuery(...);`.
4. Returned from a private method or local function whose every call invokes the result straight away: `Build()(db, id)`. Calls are matched by symbol, so a call to another overload of `Build` does not count.
5. Stored in an instance field or property of `this` from an ordinary method or accessor, without an `if` that tests that member first.

## When it stays quiet (non-goals)

- Static field and static auto-property initializers, even when they invoke the compiled delegate straight away, and assignments to static members (including in a static constructor).
- `??=` and `if (_query == null) _query = ...` lazy initialization.
- Instance field and property initializers that store the delegate, and assignments in instance constructors. Whether the instance lives long enough to reuse the delegate (a singleton, or a scoped service built per request) is not known, so the rule does not guess.
- Lambdas passed as the add-value factory of a cache that runs it once per key, when the compile call sits directly in that lambda rather than in a delegate nested inside it. The update factory of `AddOrUpdate` runs every time the key exists, and a lambda passed as the cached value itself is what the cache keeps, so both still report. The caches are: `GetOrAdd`, `AddOrUpdate`, `GetOrCreate`, `GetOrCreateAsync`, `LazyInitializer.EnsureInitialized` and `Lazy<T>`.
- Dictionary stores (`cache[key] = EF.CompileQuery(...)`) and the delegate passed to another method.
- Factory lambdas that return the delegate (`() => EF.CompileQuery(...)`), and public, internal or protected methods that return it, because their callers decide how long it lives. A private factory is quiet as soon as one caller does anything other than invoke the result, such as initializing a static field.
- A local that is stored, returned or passed on as well as invoked.

## Code Fix

Moves the compile call into a `private static readonly` field declared just above the member, typed with the delegate the call returns (keeping nullable annotations), and uses the field where the call was:

```csharp
// Before
public Task<Blog?> Get(int id) =>
    EF.CompileAsyncQuery((BlogContext c, int i) => c.Blogs.FirstOrDefault(b => b.Id == i))(_db, id);

// After
private static readonly Func<BlogContext, int, Task<Blog?>> GetQuery = EF.CompileAsyncQuery((BlogContext c, int i) => c.Blogs.FirstOrDefault(b => b.Id == i));

public Task<Blog?> Get(int id) => GetQuery(_db, id);
```

The field is named after the member (`GetQuery`, then `GetQuery2` and so on when the name is taken). No fix is offered when the query lambda reads a local, a parameter of the method or the instance (a static field cannot see them; pass the value as a query parameter instead), when the delegate type uses a method type parameter, when an `#if`, `#elif`, `#else` or `#endif` directive sits directly above the member (the field could land in the wrong branch), or when the rewritten document would have more compiler errors than before.

## Test Cases

### Violations

```csharp
return EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i))(_db, id);
var query = EF.CompileQuery((Ctx c) => c.Blogs.Count()); return query(_db);
public static Func<Ctx, int, Blog> ById => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));
```

### Valid

```csharp
private static readonly Func<Ctx, int, Blog> ById = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i));
private static Func<Ctx, int, Blog> _byId; // _byId ??= EF.CompileQuery(...);
Cache.GetOrAdd(key, _ => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)));
```
