# Spec: LC003 - Prefer Any() over Count() > 0

## Goal
Suggest using `Any()` instead of `Count() > 0` for existence checks on `IQueryable`.

## The Problem
`Count()` forces the database to scan all matching rows to calculate a total number. `Any()` generates an `IF EXISTS` SQL statement, which allows the database to stop as soon as it finds the first match. For large tables, `Any()` is significantly faster.

### Example Violation
```csharp
// Slow: Counts every single matching record
if (db.Users.Count(u => u.Active) > 0) { ... }
```

### The Fix
Use `Any()`.

```csharp
// Fast: Stops after finding the first match
if (db.Users.Any(u => u.Active)) { ... }
```

## Analyzer Logic

### ID: `LC003`
### Category: `Performance`
### Severity: `Warning`

### Algorithm
1.  **Target**: Binary expressions comparing a `Count()` or `LongCount()` call to 0.
2.  **Type Check**: Ensure the call is on an `IQueryable`.
