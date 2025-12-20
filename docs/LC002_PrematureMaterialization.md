# Spec: LC002 - Premature Materialization

## Goal
Detect usage of LINQ operators (like `Where`, `Select`, `OrderBy`) called *after* a materializing method (like `ToList`, `ToArray`, `AsEnumerable`).

## The Problem
Materializing an `IQueryable` (using `ToList`, `ToArray`, `AsEnumerable`, etc.) before applying filters or other query operators causes all data to be fetched from the database into memory before processing occurs. 

Additionally, this rule flags **redundant materialization** where multiple materializing methods are called in a row, adding unnecessary overhead and clutter.

### Example Violations
```csharp
// 1. Premature: Fetches every user, then filters in memory
var users = db.Users.ToList().Where(u => u.IsActive);

// 2. Redundant: AsEnumerable is redundant when followed by ToList
var users2 = db.Users.AsEnumerable().ToList();
```

### The Fix
Apply all filters before materializing the query, and remove redundant materialization calls.

```csharp
// Correct
var users = db.Users.Where(u => u.IsActive).ToList();
```

## Analyzer Logic

### ID: `LC002`
### Category: `Performance`
### Severity: `Warning`

### Algorithm
1.  **Target**: Invocations of common LINQ operators (`Where`, `Select`, etc.).
2.  **Receiver**: Check if the method is called on a materialized collection (e.g., `List`, `Array`, `IEnumerable`) that originated from an `IQueryable`.
