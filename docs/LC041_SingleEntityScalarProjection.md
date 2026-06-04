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

A primary-key lookup is exempt: `users.First(x => x.Id == id)` and the equivalent `users.Where(x => x.Id == id).First()` are both treated as a single-row-by-key fetch (not an over-fetch), so neither is reported regardless of how the key predicate is written. A non-key `Where` (e.g. `Where(x => x.IsActive)`) is not exempt and still reports.

The fixer is intentionally narrower than the analyzer. It appears for `First`, `FirstAsync`, `Single`, and `SingleAsync` because those rewrites preserve no-row behavior. It does not rewrite `FirstOrDefault`, `FirstOrDefaultAsync`, `SingleOrDefault`, or `SingleOrDefaultAsync`; projecting before those calls can turn an entity-null property access into a scalar default/null value instead of preserving the original runtime behavior.
