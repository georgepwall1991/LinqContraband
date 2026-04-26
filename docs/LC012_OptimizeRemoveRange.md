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
LC012 is conservative. It reports only when the project exposes an EF-style `ExecuteDelete()` extension and the `RemoveRange(...)` argument is still query-shaped (`IQueryable<T>`/`DbSet<T>`). It stays quiet for materialized lists, arrays, tracked entity collections, `params` entity arguments, and EF Core versions where `ExecuteDelete()` is unavailable.

The fixer is intentionally narrower than the diagnostic. It does not offer an automatic rewrite when a later `SaveChanges()`/`SaveChangesAsync()` appears in the same block, because replacing deferred tracked deletion with immediate `ExecuteDelete()` can change unit-of-work timing. Apply the optimization manually only after confirming change tracking, client-side cascades, interceptors, and deferred save semantics are not required.
