---
layout: default
title: "Spec: LC032 - Use ExecuteUpdate for Provable Bulk Scalar Updates"
---

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
    .ExecuteUpdate(setters => setters.SetProperty(u => u.Name, u => "Archived"));
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

## Code Fix

The fixer rewrites the proven loop into a single set-based `ExecuteUpdate` call, building one
`SetProperty` per assigned property by reusing the loop variable name as the lambda parameter
(so each assignment's target and value transplant verbatim), and prepends a warning comment:

```csharp
// Warning: ExecuteUpdate runs immediately and bypasses change tracking and entity callbacks.
db.Users.Where(u => u.IsActive)
    .ExecuteUpdate(setters => setters.SetProperty(user => user.Name, user => "Archived"));
db.SaveChanges();
```

| Context | Rewrite |
| --- | --- |
| Synchronous method / lambda / local function | `query.ExecuteUpdate(setters => ...);` |
| Async method / async lambda / async local function (with `ExecuteUpdateAsync` available) | `await query.ExecuteUpdateAsync(setters => ...);` |
| Async context where no `ExecuteUpdateAsync` overload exists | No fix offered |

### Behaviour

- **The trailing `SaveChanges()` is left in place.** Once the loop is gone it commits nothing for
  the converted rows (`ExecuteUpdate` already wrote them), but it still flushes any *unrelated*
  pending changes made earlier in the method. The fixer never deletes surrounding statements.
- **Inline materializers are stripped.** `foreach (var u in db.Users.Where(...).ToList())` (and the
  awaited `await db.Users.Where(...).ToListAsync()` form) rewrites against the underlying
  `IQueryable` (`ToList`/`ToArray`/`ToListAsync`/`ToArrayAsync` are peeled).
- **Duplicate property assignments collapse to the last write**, matching the loop's runtime
  last-write-wins semantics.
- **The cancellation token is preserved.** A token on the awaited `SaveChangesAsync(token)` is
  carried onto `ExecuteUpdateAsync(setters => ..., token)`, since that call becomes the actual
  database operation.
- **Async safety.** Async-ness is taken from the awaited trailing `SaveChangesAsync` (which also
  covers top-level programs that have no enclosing async method) or the nearest enclosing async
  function. The fixer prefers the awaited `ExecuteUpdateAsync` overload and declines rather than
  inject a blocking sync-over-async `ExecuteUpdate()` call (the smell `LC008` flags) when only the
  synchronous overload is available.

### When the fixer declines (the diagnostic still reports)

- **Local-variable sources** (`var users = db.Users.Where(...); foreach (var u in users)` or a
  pre-materialized `var users = await ...ToListAsync();`). Inlining the local would orphan it or
  produce a type-invalid receiver, so the v1 fixer leaves these as a manual rewrite.
- **The trailing `SaveChanges()` result is observed** (`return db.SaveChanges();`,
  `var n = db.SaveChanges();`). The leftover call would return `0` after the rewrite, so the
  affected-row count a caller reads would silently change.
- **A value reads a property written earlier in the same iteration** (e.g. a second
  `user.Total = user.Total + item`). `ExecuteUpdate` evaluates every value against the *original*
  row, so collapsing the sequential writes would change the result.
- **Async context without an `ExecuteUpdateAsync` overload** (or `SaveChangesAsync(token)` with no
  token-accepting `ExecuteUpdateAsync` overload), to avoid a blocking call or a dropped token.

### Safety contract

`ExecuteUpdate` / `ExecuteUpdateAsync` issue a single set-based SQL `UPDATE` and **bypass EF Core
change tracking, entity callbacks, save interceptors, `SavingChanges` events, and optimistic
concurrency tokens**. They also execute immediately rather than deferring to the next
`SaveChanges()`. Apply the optimization only after confirming none of those behaviours are required
for the updated rows.
