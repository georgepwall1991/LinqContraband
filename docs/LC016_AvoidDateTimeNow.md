# Spec: LC016 - Avoid DateTime.Now in Queries

## Goal
Detect usage of `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, or `DateTimeOffset.UtcNow` directly inside LINQ queries.

## The Problem
Using `DateTime.Now` inside a query prevents the database execution plan from being cached effectively because the value changes constantly. It also makes your queries harder to unit test because they depend on the system clock.

### Example Violation
```csharp
// Un-cacheable query
var activeUsers = db.Users.Where(u => u.ExpiryDate > DateTime.Now).ToList();
```

### The Fix
Extract the date to a local variable before running the query.

```csharp
// Correct: Cachable query
var now = DateTime.Now;
var activeUsers = db.Users.Where(u => u.ExpiryDate > now).ToList();
```

The fixer chooses a unique local name when `now` is already used by an enclosing method, local function, or lambda parameter.

## Analyzer Logic

### ID: `LC016`
### Category: `Performance`
### Severity: `Warning`
