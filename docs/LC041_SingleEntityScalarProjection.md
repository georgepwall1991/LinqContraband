# Spec: LC041 - Single Entity Scalar Projection

## Goal
Detect single-entity queries that materialize an entire entity when only one scalar property is consumed.

## The Problem
Calling `First*` or `Single*` to fetch a whole row and then reading one property performs more work than projecting that property directly.

### Example Violation
```csharp
var user = db.Users.First(u => u.IsActive);
Console.WriteLine(user.Name);
```

### The Fix
Project the consumed property before materializing when the materializer preserves no-row semantics.

```csharp
var user = db.Users
    .Where(u => u.IsActive)
    .Select(u => u.Name)
    .First();
Console.WriteLine(user);
```

## Analyzer Logic

### ID: `LC041`
### Category: `Performance`
### Severity: `Info`

### Notes
The analyzer reports only when a `var` local is consumed through one scalar property read in the same executable scope. It stays silent when the entity escapes, multiple properties are read, or the property is written.

The fixer is intentionally narrower than the analyzer. It appears for `First`, `FirstAsync`, `Single`, and `SingleAsync` because those rewrites preserve no-row behavior. It does not rewrite `FirstOrDefault`, `FirstOrDefaultAsync`, `SingleOrDefault`, or `SingleOrDefaultAsync`; projecting before those calls can turn an entity-null property access into a scalar default/null value instead of preserving the original runtime behavior.
