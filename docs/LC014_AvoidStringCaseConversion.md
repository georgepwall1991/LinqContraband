# Spec: LC014 - Avoid String Case Conversion in Queries

## Goal
Detect usage of `ToLower()` or `ToUpper()` on entity properties within LINQ queries.

## The Problem
Using `ToLower()` or `ToUpper()` on a database column in a `Where` clause prevents the database from using an index on that column. This forces a full table scan, which is very slow for large tables.

### Example Violation
```csharp
// Slow: Cannot use index on 'Email'
var user = db.Users.FirstOrDefault(u => u.Email.ToLower() == email.ToLower());
```

### The Fix
Use `string.Equals` with a case-insensitive comparison, or rely on the database's default case-insensitive collation.

```csharp
// Fast: Database can use index
var user = db.Users.FirstOrDefault(u => u.Email == email);
```

## Analyzer Logic

### ID: `LC014`
### Category: `Performance`
### Severity: `Warning`
