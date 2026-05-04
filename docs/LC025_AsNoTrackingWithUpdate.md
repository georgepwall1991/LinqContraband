# LC025 - Avoid AsNoTracking with Update/Remove

## Goal
Detect entities or materialized entity collections that come from an `AsNoTracking()` query and are later passed to `DbContext.Update`, `DbSet.Update`, `DbContext.Remove`, `DbSet.Remove`, their `*Range` variants, or explicit `DbContext.Entry(entity).State = EntityState.Modified | Deleted` writes.

## Why This Matters
`AsNoTracking()` is correct for read-only queries because EF Core does not keep original values in the change tracker. Passing those entities back to `Update()` forces EF Core to attach them and mark all properties as modified, which can produce wider SQL updates than intended and can hide concurrency mistakes. Passing untracked entities to `Remove()` is usually a sign that the query was marked read-only even though the result is part of a write path.

## Violations

```csharp
var user = db.Users.AsNoTracking().FirstOrDefault(u => u.Id == id);
if (user is not null)
{
    db.Users.Update(user);
}
```

```csharp
var users = db.Users.AsNoTracking().Where(u => u.Age > 20).ToList();
db.Users.UpdateRange(users);
```

```csharp
var user = db.Users.AsNoTracking().First(u => u.Id == id);
db.Entry(user).State = EntityState.Modified;
```

```csharp
foreach (var user in db.Users.AsNoTracking().Where(u => u.Age > 20).ToList())
{
    db.Users.Remove(user);
}
```

## Safe Patterns

Use a tracked query when the entity is going through a write path:

```csharp
var user = db.Users.FirstOrDefault(u => u.Id == id);
if (user is not null)
{
    user.Name = "Updated";
    db.SaveChanges();
}
```

Keep `AsNoTracking()` for read-only projections or materialized values that are not passed back into EF write APIs:

```csharp
var names = db.Users.AsNoTracking()
    .Where(u => u.Age > 20)
    .Select(u => u.Name)
    .ToList();
```

## Analyzer Behavior
LC025 tracks local variables within the current method, lambda, or local function. It reports when the nearest previous origin for the local is an `AsNoTracking()` query, including:

- direct entity locals materialized from a no-tracking query;
- collection locals materialized from a no-tracking query and passed to `UpdateRange` or `RemoveRange`;
- foreach iteration variables over no-tracking collections;
- entities materialized from local query aliases that were built from `AsNoTracking()`;
- explicit `Entry(entity).State = EntityState.Modified` or `EntityState.Deleted` assignments.

The `AsNoTracking()` call must resolve to EF Core's namespace boundary (`Microsoft.EntityFrameworkCore` or a child namespace). Project-local helpers with the same method name are ignored.

The rule is order-aware. It does not report when a local is reassigned from a tracked query before the write API call, and it does not look ahead to no-tracking assignments that occur after the write.

## Code Fix
The fixer removes the `AsNoTracking()` call from direct local declarations, simple assignments, and foreach collection expressions when that is the origin of the diagnostic.

It intentionally does not rewrite broader architecture choices. If the query was intentionally read-only, change the write path instead of applying the fix blindly.
