# Spec: LC025 - Avoid AsNoTracking with Update/Remove

## Goal
Detect usage of `AsNoTracking()` on queries where the resulting entities are subsequently passed to `DbContext.Update()`, `DbSet.Update()`, `DbContext.Remove()`, or `DbSet.Remove()`.

## The Problem
`AsNoTracking()` tells EF Core not to track the entities in the change tracker. This improves performance for read-only queries. However, if you then pass these untracked entities to `Update()` or `Remove()`, EF Core has to re-attach them to the context. 
1.  **For Update**: EF Core will mark ALL properties as modified because it doesn't know which ones actually changed (since it wasn't tracking the original state). This results in a SQL UPDATE statement that updates every column, which is less efficient and can cause concurrency issues.
2.  **For Remove**: It works, but it's often a sign of inconsistent intent (marking as read-only, then deleting).

### Example Violation
```csharp
// Violation: Entity is not tracked, so Update will update all columns
var user = db.Users.AsNoTracking().FirstOrDefault(u => u.Id == id);
user.Name = "New Name";
db.Users.Update(user); 
```

### The Fix
Remove `AsNoTracking()` if you intend to modify or delete the entity, so EF Core can track changes and generate optimized SQL.

```csharp
// Correct: Entity is tracked, Update (or just SaveChanges) will be optimized
var user = db.Users.FirstOrDefault(u => u.Id == id);
user.Name = "New Name";
db.SaveChanges(); // No need for Update() if tracked
```

## Analyzer Logic

### ID: `LC025`
### Category: `Reliability` (or `Performance`)
### Severity: `Warning`

### Algorithm
1.  **Detection**: This is a cross-statement analysis, which is harder with Roslyn `IOperation` but possible within a method scope.
2.  **Step 1**: Find calls to `AsNoTracking()`.
3.  **Step 2**: Track the resulting variable (data flow analysis).
4.  **Step 3**: Check if that variable (or items from the collection) is passed to `Update` or `Remove`.

*Simplification for MVP*: 
-   Focus on local variables assigned from a query ending in `AsNoTracking()`.
-   Check if that local variable is used as an argument to `Update`/`Remove` in the same method.

## Test Cases

### Violations
```csharp
var user = db.Users.AsNoTracking().First(x => x.Id == 1);
db.Users.Update(user);
```

### Valid
```csharp
var user = db.Users.First(x => x.Id == 1);
db.Users.Update(user);
```

## Implementation Plan
1.  Create `LC025_AsNoTrackingWithUpdate` directory.
2.  Implement `AsNoTrackingWithUpdateAnalyzer`.
3.  Implement tests.
