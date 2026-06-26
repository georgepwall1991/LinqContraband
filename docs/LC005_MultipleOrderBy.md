# Spec: LC005 - Multiple OrderBy Calls

## Goal
Detect usage of multiple `OrderBy` or `OrderByDescending` calls in a single query chain.

## The Problem
In LINQ, each `OrderBy` call completely resets the sort order. If you want to sort by multiple columns, you must use `ThenBy` or `ThenByDescending` after the first `OrderBy`.

### Example Violation
```csharp
// Error: The first sort by Name is discarded and ignored.
var users = db.Users.OrderBy(u => u.Name).OrderBy(u => u.Age).ToList();

// Same reset after a simple local hop.
var sorted = db.Users.OrderBy(u => u.Name);
var users = sorted.OrderBy(u => u.Age).ToList();
```

### The Fix
Use `ThenBy` or `ThenByDescending` for the second and later sort keys.

```csharp
// Correct: Sorts by Name, then by Age for ties.
var users = db.Users.OrderBy(u => u.Name).ThenBy(u => u.Age).ToList();
```

The code fix rewrites only the later resetting sort call, preserving the selector and any explicit generic type arguments.
The same fix is offered when the resetting sort is called on a single-assignment local that still has an ordered type:

```csharp
var sorted = db.Users.OrderBy(u => u.Name);
var users = sorted.ThenBy(u => u.Age).ToList();
```

If the local is explicitly widened to `IEnumerable<T>` or `IQueryable<T>`, LC005 still reports the reset, but the fix is
manual because `ThenBy` is not available on the widened receiver type. LC005 stays quiet when the local is reassigned
before the later `OrderBy` directly, through deconstruction, or through `out`/`ref`, because the analyzer cannot prove which ordering state
reaches the reset.

## Query-comprehension syntax

Two separate `orderby` clauses are the query-syntax spelling of the same reset and are flagged:

```csharp
// Warning: the second orderby resets the first.
var users = from u in db.Users
            orderby u.Name
            orderby u.Age
            select u;
```

A single `orderby` clause with multiple keys is **correct** multi-level sorting (it lowers to
`OrderBy(...).ThenBy(...)`) and is intentionally left quiet:

```csharp
// Correct: sorts by Name, then by Age for ties.
var users = from u in db.Users
            orderby u.Name, u.Age
            select u;
```

The diagnostic is **report-only** for query syntax — there is no method-call node to rewrite to
`ThenBy`, so the automatic fix is withheld there. Collapse the two clauses into one comma-separated
`orderby` to resolve it by hand. The `ThenBy` fix is still offered for the fluent
`OrderBy(...).OrderBy(...)` form.

## Boundaries

LC005 follows direct fluent chains, static `Enumerable`/`Queryable` syntax, and single-assignment sorted locals. It does
not chase fields, properties, multi-assignment locals, or arbitrary helper methods, because those shapes need broader
dataflow proof to avoid noisy warnings.

## Analyzer Logic

### ID: `LC005`
### Category: `Correctness`
### Severity: `Warning`
