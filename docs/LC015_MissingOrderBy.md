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
### Category: `Correctness` (or `Reliability`)
### Severity: `Warning` (or `Error`?)

### Algorithm
1.  **Target Methods**: Intercept invocations of:
    -   `Skip`
    -   `Last`
    -   `LastOrDefault`
    -   `Chunk` (NET 6+)
    
2.  **Type Check**: Verify the method is called on `IQueryable<T>`.
    -   Ignore `IEnumerable<T>` (in-memory objects usually preserve order).

3.  **Upstream Walk**: Walk up the invocation chain (the `Instance` or first argument for extensions) to find a sorting method.
    -   **Found**: `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`.
        -   **STOP**: Valid.
    -   **Found**: `Skip`, `Take` (recursive check upstream).
    -   **Found**: `Where`, `Select` (continue upstream).
    -   **Found**: Source (e.g. `db.Users`).
        -   **STOP**: **VIOLATION FOUND**.
    
    -   *Edge Case*: If the chain is broken (e.g. local variable assignment), try to resolve the variable definition and continue walking? 
        -   *MVP*: Just walk the fluent chain. If variable usage, maybe ignore or simple check.

### Corner Cases
1.  **OrderedQueryable**: If the type is `IOrderedQueryable<T>`, we *might* assume it's sorted. However, `IQueryable<T>` interface doesn't enforce it.
    -   Actually, `OrderBy` returns `IOrderedQueryable<T>`. `Where` returns `IQueryable<T>`.
    -   If we call `Where` after `OrderBy`, the type reverts to `IQueryable<T>` (in some definitions) or stays `IQueryable`.
    -   *Better approach*: Inspect the Operation tree (invocation chain) rather than static types, because `Where` preserves order but hides the `IOrderedQueryable` type sometimes.

2.  **Preserved Order**:
    -   `Where`, `Select` (projection might not preserve order if it's complex? No, SQL `SELECT ... WHERE` preserves `ORDER BY` if applied *after*?).
    -   Actually, in SQL: `SELECT * FROM (SELECT * FROM Users ORDER BY Id) WHERE Age > 10`. The order is *usually* preserved but technically the outer query needs an ORDER BY for guarantees?
    -   EF Core typically generates: `SELECT ... FROM ... WHERE ... ORDER BY ... OFFSET ... FETCH ...`.
    -   The `OrderBy` must be present in the LINQ expression tree sent to EF.
    
    So, checking if `OrderBy` exists *anywhere* in the chain upstream is sufficient?
    -   Wait: `db.Users.OrderBy(x => x.Id).Skip(10)` -> Valid.
    -   `db.Users.Skip(10).OrderBy(x => x.Id)` -> **INVALID**. Skip happens *before* OrderBy (logically impossible in LINQ usually, but if written, it means "Skip arbitrary 10, THEN sort the rest").
    
    **Refined Rule**: The `OrderBy` call must appear **before** (i.e., as a child/descendant node in the expression tree, or "to the left" in fluent syntax) the `Skip`/`Last` call.

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

## Implementation Plan
1.  Create `LC015_MissingOrderBy` directory.
2.  Implement `MissingOrderByAnalyzer`.
3.  Implement tests covering fluent chains and mixed operators (`Where`, `Select`).

