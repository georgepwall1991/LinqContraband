# Spec: LC007 - N+1 Query in Loop

## Goal
Detect database queries (like `Find`, `First`, `ToList`) executed inside a loop.

## The Problem
Executing a query inside a loop is a classic performance anti-pattern. If you have 100 items, you make 100 separate round-trips to the database. This is orders of magnitude slower than fetching all 100 items in a single query.

### Example Violation
```csharp
foreach (var id in ids)
{
    // Hits the database for every single iteration
    var user = db.Users.Find(id);
}
```

### The Fix
Fetch all required data in bulk outside the loop.

```csharp
// Correct: One query for all matching records
var users = db.Users.Where(u => ids.Contains(u.Id)).ToList();
```

## Analyzer Logic

### ID: `LC007`
### Category: `Performance`
### Severity: `Warning`
