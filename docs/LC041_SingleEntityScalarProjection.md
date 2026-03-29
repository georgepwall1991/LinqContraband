# Spec: LC041 - Single Entity Scalar Projection

## Goal
Detect single-entity queries that materialize an entire entity when only one scalar property is consumed.

## The Problem
Calling `First*` or `Single*` to fetch a whole row and then reading one property performs more work than projecting that property directly.

### Example Violation
```csharp
var user = db.Users.FirstOrDefault(u => u.IsActive);
Console.WriteLine(user.Name);
```

### The Fix
Project the consumed property before materializing.

```csharp
var user = db.Users
    .Where(u => u.IsActive)
    .Select(u => u.Name)
    .FirstOrDefault();
Console.WriteLine(user);
```

## Analyzer Logic

### ID: `LC041`
### Category: `Performance`
### Severity: `Info`

### Notes
The fixer is intentionally guarded. It appears only for `var` locals whose downstream usage is proven to be a single scalar property read.
