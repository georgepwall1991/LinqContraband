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
LC012 is conservative. It reports only when the `RemoveRange(...)` argument is still query-shaped (`IQueryable<T>`/`DbSet<T>`) so the fixer can safely rewrite that source to `ExecuteDelete()`. It stays quiet for materialized lists, arrays, tracked entity collections, and `params` entity arguments because `ExecuteDelete()` bypasses change tracking, client-side cascades, and in-memory state.
