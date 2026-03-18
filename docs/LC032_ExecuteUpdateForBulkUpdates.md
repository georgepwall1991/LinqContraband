# Spec: LC032 - Use ExecuteUpdate for Provable Bulk Scalar Updates

## Goal
Detect tracked bulk-update loops that can be replaced with `ExecuteUpdate()` or `ExecuteUpdateAsync()`.

## The Problem
Looping through tracked entities, assigning scalar properties one by one, and then calling `SaveChanges()` forces EF Core to materialize and track every row before issuing per-entity updates. `ExecuteUpdate()` performs the update as a single set-based SQL statement, which can be dramatically faster for bulk changes.

### Example Violation
```csharp
using var db = new AppDbContext();

foreach (var user in db.Users.Where(u => u.IsActive))
{
    user.Name = "Archived";
}

db.SaveChanges();
```

### The Fix
Use `ExecuteUpdate()` when the update is a uniform scalar change and bypassing change tracking is acceptable.

```csharp
db.Users
    .Where(u => u.IsActive)
    .ExecuteUpdate(setters => setters.SetProperty(u => u.Name, "Archived"));
```

## Analyzer Logic

### ID: `LC032`
### Category: `Performance`
### Severity: `Info`

### Algorithm
1. Target `SaveChanges()` / `SaveChangesAsync()` calls on a local `DbContext`.
2. Require the immediately previous statement to be a `foreach` loop in the same executable root.
3. Prove the loop source comes from the same local `DbContext` through:
   - a direct `DbSet` / queryable chain, or
   - a single-assignment local whose initializer comes from that query.
4. Require the loop body to contain only direct scalar property assignments on the iteration variable.
5. Skip any ambiguous or behavior-changing cases such as:
   - navigation or collection mutations,
   - helper calls or branching inside the loop,
   - field/parameter provenance,
   - different read and write contexts,
   - projects where `ExecuteUpdate` is not available.

## Notes
`ExecuteUpdate` bypasses change tracking and entity callbacks. This rule is advisory only and intentionally avoids an automatic fixer in v1.
