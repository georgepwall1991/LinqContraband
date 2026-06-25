# LC002: Premature Materialization

## Goal
Catch query work that moves from the provider to LINQ-to-Objects only because an `IQueryable` was materialized too early, and catch redundant second materializers layered on top of an already materialized query.

## What LC002 Reports

### 1. Premature query continuation
LC002 reports approved `Enumerable` operators that run in the same fluent chain after a materializer or client boundary sourced from `IQueryable`.
Lambda-based continuations must also pass a conservative provider-safety check before the rule reports.

```csharp
var users = db.Users.ToList().Where(u => u.Age > 18);
var count = db.Users.ToArray().Count();
var ordered = db.Users.AsEnumerable().OrderBy(u => u.Name);
```

`ToList()` and `ToArray()` execute the provider query immediately and create an in-memory snapshot. `AsEnumerable()` is different: it does not fetch rows by itself, but it changes the rest of the chain to `Enumerable`, so the next `Where`, `OrderBy`, `Count`, or similar operator can no longer be translated by the provider.

LC002 reports these continuation families when they follow an inline provider-sourced boundary:

- sequence operators: `Where`, `Select`, `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`, `Skip`, and `Take`
- terminal operators: `Count`, `LongCount`, `Any`, `All`, `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault`, `Min`, `Max`, `Sum`, and `Average`

### 2. Redundant materialization
LC002 reports a second direct collection materializer when the sequence was already materialized in the same chain from `IQueryable` **and collapsing the pair does not change the result**.

```csharp
var usersAgain = db.Users.ToArray().ToList();   // ToArray is redundant; collapses to ToList()
var distinct = db.Users.ToList().ToHashSet();    // ToList is redundant; collapses to ToHashSet()
```

A de-duplicating set materializer (`ToHashSet`, `ToImmutableHashSet`, `ToImmutableSortedSet`) as the **source** of a second materializer is **not** redundant and is left quiet. The fix removes the source call, which would silently drop de-duplication (`ToHashSet().ToList()`) or drop a custom equality comparer (`ToHashSet(StringComparer.OrdinalIgnoreCase).ToHashSet()`):

```csharp
var distinctList = db.Users.ToHashSet().ToList();                                  // NOT reported — ToHashSet de-duplicates
var ci = db.Users.Select(u => u.Name).ToHashSet(StringComparer.OrdinalIgnoreCase).ToHashSet(); // NOT reported — comparer would be lost
```

A keyed (`ToDictionary`) or grouped (`ToLookup`) materializer as the **source** of a second materializer is likewise **not** redundant: the source produces a structure whose element type differs from the sequence, so the trailing call transforms its shape rather than re-materializing the same data. Removing the source would change the result type, so these are left quiet:

```csharp
var pairs = db.Users.ToDictionary(u => u.Id).ToList();   // NOT reported — yields List<KeyValuePair<int, User>>
var groups = db.Users.ToLookup(u => u.Age).ToList();     // NOT reported — yields List<IGrouping<int, User>>
```

## Provider-Safe Continuation Gate
LC002 only reports lambda continuations when the lambda looks safe to keep in the provider query. Simple member access, captured scalar values, comparisons, boolean expressions, tuples, anonymous objects, and basic string calls such as `Contains`, `StartsWith`, `EndsWith`, `IsNullOrEmpty`, and `IsNullOrWhiteSpace` are eligible:

```csharp
var adults = db.Users.ToList().Where(u => u.Age >= minAge);       // LC002
var names = db.Users.ToArray().Select(u => new { u.Id, u.Name }); // LC002
var matches = db.Users.ToList().Where(u => u.Name.Contains(term)); // LC002
```

Client-only or ambiguous delegates stay quiet because moving them before materialization could change behaviour or fail translation:

```csharp
var local = db.Users.ToList().Where(u => IsActive(u)); // no LC002
var regex = db.Users.ToList().Where(u => Regex.IsMatch(u.Name, pattern)); // no LC002
var ordinal = db.Users.ToList().Where(u => u.Name.Contains(term, StringComparison.OrdinalIgnoreCase)); // no LC002
var indexed = db.Users.ToList().Where((u, index) => index > 0); // no LC002
```

## Intentional Client Boundaries
Materializing early can be the right design when the rest of the work is deliberately client-side: custom comparers, non-translatable helpers, regex, snapshot reuse, or logic that must run after data leaves the provider. Keep that boundary visible by ending the provider query first, then continuing from a named local:

```csharp
var snapshot = db.Users
    .Where(u => u.IsActive)
    .ToList();

var orderedForDisplay = snapshot
    .OrderBy(u => NaturalSortKey(u.Name))
    .ToList();
```

LC002 intentionally avoids chasing locals, fields, properties, constructor materialization, and control-flow-dependent assignments. That keeps the rule focused on obvious inline chains and avoids second-guessing already separated in-memory workflows.

## What LC002 Intentionally Ignores
- Ambiguous provenance such as multiple local assignments or control-flow-dependent sources
- Already materialized locals, aliases, constructors, arrays, DTO lists, fields, properties, and other post-processing shapes where in-memory work may be intentional
- `AsEnumerable()` by itself when it is used only as an explicit client boundary
- Overloads or lambdas that do not have a clear `IQueryable`-safe equivalent, such as index-aware predicates/selectors, comparer-based overloads, delegated predicates, local/source methods, `Regex`, or `StringComparison` string calls
- A de-duplicating set materializer (`ToHashSet`, `ToImmutableHashSet`, `ToImmutableSortedSet`) used as the source of a second materializer (`ToHashSet().ToList()`, `ToImmutableHashSet().ToArray()`, `ToHashSet(comparer).ToHashSet()`), where removing the source would drop de-duplication or a custom comparer
- A keyed (`ToDictionary`) or grouped (`ToLookup`) materializer used as the source of a second materializer (`ToDictionary().ToList()`, `ToLookup().ToList()`), where the trailing call transforms the keyed/grouped shape and is not a redundant re-materialization
- Pure in-memory sequences that never came from `IQueryable`

## Fixer Behavior
The fixer is intentionally conservative.

- It offers `Move query operator before materialization` only for analyzer-proven inline chains where the rewrite is explicit.
- For sequence continuations, it keeps the final materializer when needed: `ToList().Where(...)` becomes `Where(...).ToList()`, while an outer materializer such as `.ToArray()` remains the final shape.
- For terminal continuations, it removes the early materializer entirely: `ToList().Count()` becomes `Count()`.
- It offers `Remove redundant materialization` only for direct redundant materializer pairs whose collapse preserves the result; it never collapses a de-duplicating set away (`ToHashSet().ToList()` is left untouched).
- It does not offer a fix for local-hop, constructor, ambiguous, or shape-changing cases such as `ToDictionary(...).Where(...)`.
- It does not offer a fix for client-only lambda bodies because LC002 suppresses those diagnostics entirely.

## Example Fixes

```csharp
// Before
var adults = db.Users.ToList().Where(u => u.Age >= 18);

// After
var adults = db.Users.Where(u => u.Age >= 18).ToList();
```

```csharp
// Before
var usersAgain = db.Users.ToArray().ToList();

// After
var usersAgain = db.Users.ToList();
```
