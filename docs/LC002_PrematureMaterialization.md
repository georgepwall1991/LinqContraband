# Spec: LC002 - Premature Materialization

## Goal
Detect usage of LINQ operators (like `Where`, `Select`, `OrderBy`) called *after* a materializing method (like `ToList`, `ToArray`, `AsEnumerable`).

## The Problem
Materializing methods execute the SQL query and bring the results into memory. If you apply filters *after* materializing, the database returns every row, and your application filters them in memory. This is highly inefficient.

### Example Violation
```csharp
// Fetches every user, then filters in memory
var users = db.Users.ToList().Where(u => u.IsActive);
```

### The Fix
Apply all filters before materializing the query.

```csharp
// Correct: Database filters the rows
var users = db.Users.Where(u => u.IsActive).ToList();
```

## Analyzer Logic

### ID: `LC002`
### Category: `Performance`
### Severity: `Warning`

### Algorithm
1.  **Target**: Invocations of common LINQ operators (`Where`, `Select`, etc.).
2.  **Receiver**: Check if the method is called on a materialized collection (e.g., `List`, `Array`, `IEnumerable`) that originated from an `IQueryable`.
