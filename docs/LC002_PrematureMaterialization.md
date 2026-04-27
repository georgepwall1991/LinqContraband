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

### 2. Redundant materialization
LC002 reports a second direct collection materializer when the sequence was already materialized in the same chain from `IQueryable`.

```csharp
var usersAgain = db.Users.ToArray().ToList();
```

## What LC002 Intentionally Ignores
- Ambiguous provenance such as multiple local assignments or control-flow-dependent sources
- Already materialized locals, aliases, constructors, arrays, DTO lists, fields, properties, and other post-processing shapes where in-memory work may be intentional
- `AsEnumerable()` by itself when it is used only as an explicit client boundary
- Overloads or lambdas that do not have a clear `IQueryable`-safe equivalent, such as index-aware predicates/selectors, comparer-based overloads, delegated predicates, local/source methods, `Regex`, or `StringComparison` string calls
- Pure in-memory sequences that never came from `IQueryable`

## Fixer Behavior
The fixer is intentionally conservative.

- It offers `Move query operator before materialization` only for analyzer-proven inline chains where the rewrite is explicit.
- It offers `Remove redundant materialization` only for direct redundant materializer pairs.
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
