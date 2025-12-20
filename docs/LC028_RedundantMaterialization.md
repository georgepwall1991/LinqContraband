# Spec: LC028 - Redundant Materialization

## Goal
Detect redundant calls to `AsEnumerable()` or `ToList()` on an `IQueryable` that is immediately materialized again.

## The Problem
Calling multiple materializers in a row (e.g., `db.Users.AsEnumerable().ToList()`) is redundant and slightly inefficient. `AsEnumerable()` transitions the query to client-side evaluation, and `ToList()` then executes it and copies it into a list. If you're going to call `ToList()`, there's no need for `AsEnumerable()` first. Similarly, `db.Users.ToList().ToList()` is just wasted work.

### Example Violation
```csharp
// Violation: AsEnumerable is redundant here
var users = db.Users.AsEnumerable().ToList();

// Violation: Double ToList
var users2 = db.Users.ToList().ToList();
```

### The Fix
Remove the redundant call.

```csharp
// Correct
var users = db.Users.ToList();
```

## Analyzer Logic

### ID: `LC028`
### Category: `Performance`
### Severity: `Warning`

### Algorithm
1.  **Target Methods**: Intercept invocations of `ToList`, `ToArray`, `AsEnumerable`, `ToDictionary`, `ToHashSet`.
2.  **Upstream Check**: Check if the immediate parent call in the chain is ALSO a materializer.
    -   `ToList()` after `AsEnumerable()` -> Flag `AsEnumerable` as redundant.
    -   `ToList()` after `ToList()` -> Flag second `ToList` as redundant.

## Test Cases

### Violations
```csharp
query.AsEnumerable().ToList();
query.ToList().ToArray();
```

### Valid
```csharp
query.Where(x => x.Active).ToList();
```

## Implementation Plan
1.  Create `LC028_RedundantMaterialization` directory.
2.  Implement `RedundantMaterializationAnalyzer`.
3.  Implement tests.
