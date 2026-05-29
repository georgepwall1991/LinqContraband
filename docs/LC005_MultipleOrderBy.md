# Spec: LC005 - Multiple OrderBy Calls

## Goal
Detect usage of multiple `OrderBy` or `OrderByDescending` calls in a single query chain.

## The Problem
In LINQ, each `OrderBy` call completely resets the sort order. If you want to sort by multiple columns, you must use `ThenBy` or `ThenByDescending` after the first `OrderBy`.

### Example Violation
```csharp
// Error: The first sort by Name is discarded and ignored.
var users = db.Users.OrderBy(u => u.Name).OrderBy(u => u.Age).ToList();
```

### The Fix
Use `ThenBy` or `ThenByDescending` for the second and later sort keys.

```csharp
// Correct: Sorts by Name, then by Age for ties.
var users = db.Users.OrderBy(u => u.Name).ThenBy(u => u.Age).ToList();
```

The code fix rewrites only the later resetting sort call, preserving the selector and any explicit generic type arguments.

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

## Analyzer Logic

### ID: `LC005`
### Category: `Correctness`
### Severity: `Warning`
