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
4. Returned from a private method or local function whose every call invokes the result straight away: `Build()(db, id)`. Calls are matched by symbol, so a call to another overload of `Build` does not count. A factory whose only callers are static field or property initializers or a static constructor runs once and stays quiet.
5. Stored in an instance field or property of `this` or `base` from an ordinary method or accessor, without an `if` whose whole condition guarantees that member is null in the branch holding the store (`if (_query == null)`, `if (_query is null && ready)`, or the `else` of `if (_query != null)` or `if (_query != null || other)`). A `!= null` guard, `if (_query == null || refresh)`, the `else` of `if (_query != null && ready)`, a guard on another member, or a guard on a local that shares the member's name still reports.

## When it stays quiet (non-goals)

- Static field and static auto-property initializers, even when they invoke the compiled delegate straight away, and assignments to static members (including in a static constructor).
- `??=` and `if (_query == null) _query = ...` lazy initialization.
- A `Lazy<T>` or `AsyncLazy<T>` stored straight into a field or property of this instance (unqualified, `this.` or `base.`) or a static one, and kept there: a field or property initializer, a constructor, `??=`, a null guard, or a static member, judged the same way as a compiled query stored directly. An unguarded `_lazy = new Lazy<...>(...)` in an ordinary method replaces it on every call and reports, and so does a store on another object (`holder.Query = new Lazy<...>(...)`), whose lifetime is not known. Any other use may build it on every call and reports: `.Value`, `.GetValueAsync()`, `.Task`, a local, an argument or a return value.
- Instance field and property initializers that store the delegate, and assignments in instance constructors. Whether the instance lives long enough to reuse the delegate (a singleton, or a scoped service built per request) is not known, so the rule does not guess.
- Lambdas passed as the add-value factory of a cache that runs it once per key and is kept outside the call, when the compile call sits directly in that lambda rather than in a delegate nested inside it. A cast or parentheses around the factory (`(Func<string, Blog>)(_ => ...)`) and named arguments in any order (`EnsureInitialized(valueFactory: ..., target: ref _query)`) are read the same way. The cache (the receiver of `GetOrAdd`, `GetOrCreate` or `GetOrCreateAsync`) must be a field, auto-property or parameter, such as an injected `IMemoryCache` (a property with a getter body, such as `Cache => new()`, may build a new cache on every read and reports); the `ref` target of `EnsureInitialized` or `ImmutableInterlocked.GetOrAdd` must be a field or a `ref` parameter. Any other cache reports, including a new instance and any local, even one copied from a field. The update factory of `AddOrUpdate` runs every time the key exists, and a lambda passed as the cached value itself is what the cache keeps, so both still report. The caches are matched by symbol, so a project's own method of the same name, which may run the factory on every call, still reports: `ConcurrentDictionary<TKey, TValue>.GetOrAdd` and `AddOrUpdate`, `ImmutableInterlocked.GetOrAdd`, `LazyInitializer.EnsureInitialized`, the `IMemoryCache` extensions `GetOrCreate` and `GetOrCreateAsync`, `HybridCache.GetOrCreateAsync`, `System.Lazy<T>`, and the `AsyncLazy<T>` types of Microsoft.VisualStudio.Threading and Nito.AsyncEx.
- Dictionary stores (`cache[key] = EF.CompileQuery(...)`) and the delegate passed to another method.
- Factory lambdas that return the delegate (`() => EF.CompileQuery(...)`), and public, internal or protected methods that return it, because their callers decide how long it lives. A private factory is quiet as soon as one caller does anything other than invoke the result, such as initializing a static field.
- A local that is stored, returned or passed on as well as invoked.

## Code Fix

Moves the compile call into a `private static readonly` field declared as the first member of the type (after any compiled-query fields it already hoisted), typed with the delegate the call returns (keeping nullable annotations), and uses the field where the call was. Static field initializers run in source order, so a field at the top is set before any other static initializer runs, including one that reaches the member through another method:

```csharp
// Before
public Task<Blog?> Get(int id) =>
    EF.CompileAsyncQuery((BlogContext c, int i) => c.Blogs.FirstOrDefault(b => b.Id == i))(_db, id);

// After (the field is the first member of the class)
private static readonly Func<BlogContext, int, Task<Blog?>> GetQuery = EF.CompileAsyncQuery((BlogContext c, int i) => c.Blogs.FirstOrDefault(b => b.Id == i));

// ...

public Task<Blog?> Get(int id) => GetQuery(_db, id);
```

The field is named after the member (`GetQuery`, then `GetQuery2` and so on when the name is taken). No fix is offered when the query lambda reads a local, a parameter of the method or the instance (a static field cannot see them; pass the value as a query parameter instead), when it reads a static member of the type (its order against the new field is not known), when the delegate type uses a method type parameter, when the member sits inside an `#if`, `#elif` or `#else` region opened in the type before it (the field at the top would stay active in configurations that leave the member out), when another partial declaration of the type has any static field or property initializer or static constructor (the order across parts is not defined), when the first member of the type has a preprocessor directive in front of it (such as `#nullable enable`, which the new field would sit outside of), when the member and the top of the type are in different `#nullable` contexts, or when the rewritten document would have more compiler errors than before.

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
