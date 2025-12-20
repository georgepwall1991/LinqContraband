# Spec: LC009 - Missing AsNoTracking in Read Path

## Goal
Suggest using `AsNoTracking()` for queries that only read data and do not modify entities.

## The Problem
By default, EF Core tracks every entity it fetches so it can detect changes. This tracking process consumes CPU and memory. For read-only operations (like a search page or a dashboard), this overhead is wasted and slows down your application.

### Example Violation
```csharp
public List<User> GetActiveUsers()
{
    // Fetches and tracks users, even if we only display them
    return db.Users.Where(u => u.Active).ToList();
}
```

### The Fix
Add `.AsNoTracking()` to the query.

```csharp
public List<User> GetActiveUsers()
{
    // Fast read-only query
    return db.Users.AsNoTracking().Where(u => u.Active).ToList();
}
```

## Analyzer Logic

### ID: `LC009`
### Category: `Performance`
### Severity: `Warning`
