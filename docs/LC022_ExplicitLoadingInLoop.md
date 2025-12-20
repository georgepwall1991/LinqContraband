# Spec: LC022 - Avoid Explicit Loading in Loops (N+1 Problem)

## Goal
Detect usage of EF Core explicit loading methods (`Load()`, `Reference().Load()`, `Collection().Load()`) inside loops. This is a classic N+1 performance problem where a separate database round-trip is made for each item in a collection.

## The Problem
Explicit loading is useful when you have a single entity and need to load its related data later. However, when used inside a loop over a collection of entities, it results in many small database queries. This can be much slower than using `Include()` or `ThenInclude()` to load the related data in a single join query.

### Example Violation
```csharp
var blogs = db.Blogs.ToList();
foreach (var blog in blogs)
{
    // Violation: N+1 queries. Each iteration hits the database.
    db.Entry(blog).Collection(b => b.Posts).Load(); 
}
```

### The Fix
Use eager loading with `Include()` or project the data you need using `Select()`.

```csharp
// Correct: Eagerly load posts in a single query
var blogs = db.Blogs.Include(b => b.Posts).ToList();
```

## Analyzer Logic

### ID: `LC022`
### Category: `Performance`
### Severity: `Warning`

### Algorithm
1.  **Target Methods**: Intercept invocations of:
    -   `Load`
    -   `LoadAsync`
2.  **Type Check**: Ensure the method is called on an `EntityEntry`, `ReferenceEntry`, or `CollectionEntry` from EF Core.
3.  **Context Check**: Verify the invocation is nested inside a loop (`for`, `foreach`, `while`, `do-while`, or `await foreach`).

## Test Cases

### Violations
```csharp
foreach (var item in items) { db.Entry(item).Reference(x => x.Owner).Load(); }
while (condition) { entry.Collection(x => x.Tags).LoadAsync(); }
```

### Valid
```csharp
var blogs = db.Blogs.Include(b => b.Posts).ToList();
db.Entry(blog).Reference(x => x.Owner).Load(); // Not in a loop
```

## Implementation Plan
1.  Create `LC022_ExplicitLoadingInLoop` directory.
2.  Implement `ExplicitLoadingInLoopAnalyzer`.
3.  Implement tests.
