# Spec: LC007 - N+1 Query in Loop

## Goal
Detect database queries (like `Find`, `First`, `ToList`) executed inside a loop.

## The Problem
Executing a query inside a loop is a classic performance anti-pattern. If you have 100 items, you make 100 separate round-trips to the database. This is orders of magnitude slower than fetching all 100 items in a single query.

This rule also flags usage of **Explicit Loading** methods like `.Reference()` or `.Collection()` inside loops, as these are often used to trigger additional database queries for each item.

### Example Violation
```csharp
// 1. Direct Query in Loop
foreach (var id in ids)
{
    var user = db.Users.Find(id);
}

// 2. Explicit Loading in Loop
foreach (var user in db.Users.ToList())
{
    db.Entry(user).Collection(u => u.Orders).Load();
}
```

### The Fix
Fetch all required data in bulk outside the loop, or use `.Include()` for eager loading.

```csharp
// Correct: Eagerly load in one query
var users = db.Users.Include(u => u.Orders).ToList();
```

## Analyzer Logic

### ID: `LC007`
### Category: `Performance`
### Severity: `Warning`
