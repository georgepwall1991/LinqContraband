# Spec: LC015 - Ensure OrderBy Before Skip/Take

## Goal
Detect usages of `Skip()`, `Last()`, `LastOrDefault()`, or `Chunk()` on an `IQueryable` that has NOT been ordered. Without an explicit ordering, the database does not guarantee the order of results, making pagination (Skip/Take) unpredictable and non-deterministic.

## The Problem
When you paginate data (e.g., "Get Page 2") or ask for the "Last" item, you implicitly assume the data is sorted. If it's not, the database is free to return rows in any order. This leads to:
1.  **Flaky Pagination**: Users see the same item on Page 1 and Page 2, or miss items entirely.
2.  **Unpredictable Results**: `Last()` might return different results on consecutive runs.

Additionally, this rule flags **misplaced OrderBy** calls that happen *after* pagination (e.g., `.Skip(10).OrderBy(x => x)`). This is usually a bug because it sorts only the returned page, not the entire dataset.

### Example Violations
```csharp
// 1. Unordered Pagination: Which 10 users are skipped? It's random.
var page2 = db.Users.Skip(10).Take(10).ToList();

// 2. Misplaced Sorting: Gets 10 random users, THEN sorts them.
var page3 = db.Users.Take(10).OrderBy(u => u.Name).ToList();
```

### The Fix
Always call `OrderBy` or `OrderByDescending` before `Skip` or `Take`.

```csharp
// Correct: Explicitly sort all users by ID before taking a page.
var page2 = db.Users.OrderBy(u => u.Id).Skip(10).Take(10).ToList();
```

## Analyzer Logic

### ID: `LC015`
### Category: `Reliability`
### Severity: `Warning`

### Notes
LC015 checks `IQueryable<T>` chains where pagination or "last row" operators depend on a deterministic order. The order must be established upstream of the reported operator; an `OrderBy` after `Skip` or `Take` still leaves the page selection nondeterministic.

## Test Cases

### Violations
```csharp
db.Users.Skip(10);
db.Users.Where(x => x.Active).Skip(5);
db.Users.Last();
db.Users.Select(x => x.Name).Chunk(10);
```

### Valid
```csharp
db.Users.OrderBy(x => x.Id).Skip(10);
db.Users.OrderByDescending(x => x.Date).Last();
db.Users.OrderBy(x => x.Id).Where(x => x.Active).Skip(10); // Valid, order preserved through Where
db.Users.OrderBy(x => x.Id).Select(x => x.Name).Skip(10); // Valid
```
