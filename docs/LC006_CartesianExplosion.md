# Spec: LC006 - Cartesian Explosion Risk

## Goal
Detect sibling collection `Include` paths on the same query without an effective `AsSplitQuery()`.

## The Problem
When you `Include` multiple sibling collections in a single SQL query, the database generates a Cartesian product. For example, if a User has 10 Orders and 10 Roles, the query returns 100 rows for that single User. A linear nested path such as `Users -> Orders -> Items` is not the same shape and is not reported by LC006.

### Example Violation
```csharp
// Explosion Risk: Fetches Users * Orders * Roles
var users = db.Users.Include(u => u.Orders).Include(u => u.Roles).ToList();
```

### The Fix
Use `.AsSplitQuery()` to fetch each collection in its own separate SQL query.

```csharp
// Correct: Fetches Users, then Orders, then Roles (3 clean queries)
var users = db.Users.AsSplitQuery().Include(u => u.Orders).Include(u => u.Roles).ToList();
```

## Analyzer Logic

LC006 resolves lambda, filtered, and literal string include paths when the navigation symbols are provable. It deduplicates repeated include paths and reports once for each risky query chain. A final explicit `AsSplitQuery()` suppresses the diagnostic; a final explicit `AsSingleQuery()` keeps it active.

### ID: `LC006`
### Category: `Performance`
### Severity: `Warning`
