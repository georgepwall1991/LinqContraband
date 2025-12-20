# Spec: LC004 - IQueryable to IEnumerable Leak

## Goal
Detect when an `IQueryable` is passed to a method parameter of type `IEnumerable`, causing implicit materialization and preventing further query composition.

## The Problem
`IQueryable` represents a database query that hasn't run yet. `IEnumerable` usually represents an in-memory collection. If you pass an `IQueryable` to an `IEnumerable` parameter, the query provider might lose the ability to add more filters (like `Where` or `Take`) to the SQL, forcing them to run in your app instead.

### Example Violation
```csharp
public void ProcessUsers(IEnumerable<User> users) { ... }

// Leak: Query is materialized implicitly when ProcessUsers iterates
ProcessUsers(db.Users); 
```

### The Fix
Change the method parameter to `IQueryable` if you want to allow further filtering, or explicitly call `.ToList()` if you intend to fetch the data.

```csharp
// Correct: Allows composing the query inside the method
public void ProcessUsers(IQueryable<User> users) { ... }
```

## Analyzer Logic

### ID: `LC004`
### Category: `Performance`
### Severity: `Warning`
