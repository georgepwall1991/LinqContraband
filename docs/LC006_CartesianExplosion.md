# Spec: LC006 - Cartesian Explosion Risk

## Goal
Detect usage of multiple `Include` calls for collection properties without using `AsSplitQuery()`.

## The Problem
When you `Include` multiple related collections in a single SQL query, the database generates a Cartesian product. For example, if a User has 10 Orders and 10 Roles, the query returns 100 rows for that single User. This can rapidly "explode" and crash your application or the database.

### Example Violation
```csharp
// Explosion Risk: Fetches Users * Orders * Roles
var users = db.Users.Include(u => u.Orders).Include(u => u.Roles).ToList();
```

### The Fix
Use `.AsSplitQuery()` to fetch each collection in its own separate SQL query.

```csharp
// Correct: Fetches Users, then Orders, then Roles (3 clean queries)
var users = db.Users.Include(u => u.Orders).AsSplitQuery().Include(u => u.Roles).ToList();
```

## Analyzer Logic

### ID: `LC006`
### Category: `Performance`
### Severity: `Warning`
