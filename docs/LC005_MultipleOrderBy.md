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
Use `ThenBy`.

```csharp
// Correct: Sorts by Name, then by Age for ties.
var users = db.Users.OrderBy(u => u.Name).ThenBy(u => u.Age).ToList();
```

## Analyzer Logic

### ID: `LC005`
### Category: `Correctness`
### Severity: `Warning`
