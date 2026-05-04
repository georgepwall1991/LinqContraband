# Spec: LC012 - Optimize Bulk Delete with ExecuteDelete

## Goal
Suggest using `ExecuteDelete()` instead of `RemoveRange()` for bulk deletions.

## The Problem
`RemoveRange()` requires you to fetch all entities into memory before marking them for deletion. For large sets of data, this is very slow. `ExecuteDelete()` (available in EF Core 7+) performs a direct SQL `DELETE` on the database without loading any data into memory.

### Example Violation
```csharp
var oldLogs = db.Logs.Where(l => l.Date < oneYearAgo);
db.Logs.RemoveRange(oldLogs);
```

### The Fix
Use `ExecuteDelete()`.

```csharp
// Fast: Direct SQL execution
db.Logs.Where(l => l.Date < oneYearAgo).ExecuteDelete();
```

## Analyzer Logic

### ID: `LC012`
### Category: `Performance`
### Severity: `Warning`

### Notes
LC012 is conservative. It reports only when the project exposes an EF Core `ExecuteDelete()` extension from the real `Microsoft.EntityFrameworkCore` namespace and the single `RemoveRange(...)` argument is still query-shaped (`IQueryable<T>`/`DbSet<T>`). It stays quiet for materialized lists, arrays, tracked entity collections, mixed/multiple `params` entity arguments, custom or lookalike `ExecuteDelete()` helpers, EF Core versions where `ExecuteDelete()` is unavailable, and calls followed by `SaveChanges()`/`SaveChangesAsync()` later in the same executable body.

The diagnostic and fixer both avoid later-save unit-of-work shapes, because replacing deferred tracked deletion with immediate `ExecuteDelete()` can change timing. Apply the optimization manually only after confirming change tracking, client-side cascades, interceptors, and deferred save semantics are not required.
