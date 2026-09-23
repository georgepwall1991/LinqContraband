---
layout: default
title: "LC001: Local Method Usage in IQueryable"
description: "LC001 flags your own helper methods inside EF Core IQueryable lambdas that SQL cannot translate, which forces client-side evaluation or a runtime error."
---

# LC001: Local Method Usage in IQueryable

## In Plain Terms

Imagine hiring a translator to translate a book into Spanish, but you used
made-up slang words they don't know. They can't finish the job, so they hand you the *entire* dictionary and say "You
figure it out." You have to read the whole dictionary just to find one word.

## What It Flags

LC001 reports source-defined helper methods inside translation-critical `IQueryable` lambdas when the helper depends on the query row.

```csharp
var users = db.Users
    .Where(u => CalculateAge(u.DateOfBirth) > 18)
    .ToList();
```

EF Core cannot translate `CalculateAge` unless you explicitly map it to SQL. Depending on provider/version and query shape, the query either throws at runtime or crosses into client evaluation after loading more data than intended.

## Why It Matters

The expensive part is not the helper call itself. The problem is that the helper sits inside the expression tree EF Core must translate to SQL. If the provider cannot translate the method, filtering, ordering, grouping, or aggregation may happen outside the database.

That can turn a selective query into an unbounded read:

```csharp
var activeAdults = db.Users
    .Where(u => IsActive(u) && CalculateAge(u.DateOfBirth) >= 18)
    .ToList();
```

## Preferred Fixes

Move row-independent calculations outside the query and compare with queryable values.

```csharp
var minBirthDate = now.AddYears(-18);

var users = db.Users
    .Where(u => u.DateOfBirth <= minBirthDate)
    .ToList();
```

Use a translatable expression, mapped database function, computed column, value converter, or provider-specific API when the logic must run in SQL.

```csharp
var users = db.Users
    .Where(u => DatabaseFunctions.IsAdult(u.DateOfBirth))
    .ToList();
```

If you intentionally want client-side filtering, make that boundary explicit and as late as possible.

```csharp
var users = db.Users
    .Where(u => u.IsEnabled)
    .AsEnumerable()
    .Where(u => IsPreferred(u))
    .ToList();
```

The code fix uses this last shape. It inserts `AsEnumerable()` at the query source for extension syntax and rewrites static `Queryable` syntax to the matching `Enumerable` call with an `AsEnumerable()` source argument:

```csharp
var query = Queryable.Where(db.Users, u => IsPreferred(u));
```

becomes:

```csharp
var query = System.Linq.Enumerable.Where(db.Users.AsEnumerable(), u => IsPreferred(u));
```

For fully qualified static calls, the fix preserves the `System.Linq` qualifier. For bare `Queryable` calls or aliases to `System.Linq.Queryable`, it uses `System.Linq.Enumerable` so a user-defined `Enumerable` type cannot capture the fixed call. Reordered named static calls keep their argument order while the actual query sequence parameter receives the `AsEnumerable()` boundary, including `source:` on filtering operators and `outer:` on `Join`/`GroupJoin`. Upstream static operators that are still translatable stay on `Queryable`; for example, a static `Take` feeding the fixed `Where` remains server-side and the boundary is placed after that `Take`. Complex source expressions are parenthesized before the boundary, so an awaited source becomes `(await GetUsersAsync()).AsEnumerable()` instead of calling `AsEnumerable()` on the task. Ordered static chains are rewritten as a chain when required, so `Queryable.ThenBy(Queryable.OrderBy(...), ...)` becomes `System.Linq.Enumerable.ThenBy(System.Linq.Enumerable.OrderBy(...), ...)` instead of erasing the ordered source before `ThenBy`. Static `ThenBy` over an extension `OrderBy` source similarly rewrites the extension source to `AsEnumerable().OrderBy(...)`; if the ordered source is an opaque variable, the fixer does not offer the unsafe rewrite. Static wrappers that do not have an `Enumerable` counterpart, such as `Queryable.AsQueryable(...)`, stay on `Queryable` while the nested operator that contains the local helper is fixed.

The fixer is conservative for nested correlated query lambdas. LC001 still reports helpers inside nested subqueries, including helpers that reference only an outer query range variable, but it does not offer a partial `AsEnumerable()` rewrite when the diagnostic belongs to a nested query invocation. Moving only that inner boundary can leave an `Enumerable` subquery embedded in the outer `IQueryable` predicate instead of making the intended client-evaluation boundary clear.

Treat that fix as an explicit client-evaluation fallback, not as the best performance answer. For large tables, prefer a SQL-translatable rewrite.

## Reported Query Positions

LC001 reports local row-dependent helpers in positions that EF Core normally needs to translate:

- Filters and predicates: `Where`, `Any`, `All`, `Count`, `First`, `Single`, `Last`, `SkipWhile`, `TakeWhile`
- Ordering: `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`
- Joins and grouping keys: `Join`, `GroupJoin`, `GroupBy`
- Aggregate selectors: `Sum`, `Average`, `Min`, `Max`

It intentionally does not report top-level `Select` projection helpers. Modern EF Core can often run final projection code after the database query shape has already been translated.

## Safe Cases

LC001 stays quiet when the helper does not depend on the query row:

```csharp
var normalized = Normalize(status);
var users = db.Users.Where(u => u.Status == normalized);
```

It also stays quiet for methods marked as explicitly translatable:

```csharp
[Microsoft.EntityFrameworkCore.DbFunction]
public static bool IsAdult(DateTime dateOfBirth) => throw new NotSupportedException();
```

and for `EntityFrameworkCore.Projectables.ProjectableAttribute` methods. Lookalike attributes from other namespaces do not suppress the diagnostic.

Queries built over an in-memory collection run on LINQ to Objects, so LC001 stays quiet when the chain provably starts at `AsQueryable()` over an array or concrete collection (`List<T>`, `HashSet<T>`, ...) or at `new EnumerableQuery<T>(...)`. This is the shape unit tests, in-memory repositories and MockQueryable-style fakes use:

```csharp
var users = new List<User> { ... }.AsQueryable().BuildMock();
var adults = users.Where(u => IsAdult(u)); // no LC001: LINQ to Objects
```

A source the analyzer cannot prove in-memory still reports: an `IQueryable` parameter, field or property, a `DbSet`, `AsQueryable()` over an `IEnumerable<T>` (which may be a `DbSet` at runtime), or a local that is assigned more than once.

## Scope

LC001 is local to the current lambda and query invocation. It does not prove whole-program SQL translation, inspect provider model configuration in another assembly, or decide whether client evaluation is acceptable for a small table. When client evaluation is intentional, keep the `AsEnumerable()` boundary obvious and document the reason in the calling code.
