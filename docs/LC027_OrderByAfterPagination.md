# Spec: LC027 - OrderBy After Skip/Take

## Goal
Detect usage of `OrderBy`, `OrderByDescending`, `ThenBy`, or `ThenByDescending` that occur *after* a `Skip()` or `Take()` call in an `IQueryable` chain.

## The Problem
Calling `OrderBy` after `Skip` or `Take` is usually a logic error. It means you are taking an arbitrary subset of data and *then* sorting it, rather than sorting the whole dataset and then taking a specific page. In some database providers, this can also cause extremely inefficient queries or even runtime errors if the provider doesn't support nested ordering after offsets.

### Example Violation
```csharp
// Violation: Taking 10 random users and then sorting them.
// Likely intended to sort first, then take.
var users = db.Users.Take(10).OrderBy(u => u.Name).ToList();
```

### The Fix
Move the `OrderBy` calls before `Skip` and `Take`.

```csharp
// Correct: Sort all users, then take the top 10.
var users = db.Users.OrderBy(u => u.Name).Take(10).ToList();
```

## Analyzer Logic

### ID: `LC027`
### Category: `Correctness`
### Severity: `Warning`

### Algorithm
1.  **Target Methods**: Intercept invocations of `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`.
2.  **Type Check**: Ensure the receiver is an `IQueryable`.
3.  **Upstream Walk**: Walk up the invocation chain to find any calls to `Skip` or `Take`.
4.  **Found**: If `Skip` or `Take` is found upstream of the sorting method, report a violation.

## Test Cases

### Violations
```csharp
db.Users.Skip(10).OrderBy(x => x.Id);
db.Users.Take(5).ThenBy(x => x.Name);
```

### Valid
```csharp
db.Users.OrderBy(x => x.Id).Skip(10);
db.Users.OrderBy(x => x.Id).Take(5);
```

## Implementation Plan
1.  Create `LC027_OrderByAfterPagination` directory.
2.  Implement `OrderByAfterPaginationAnalyzer`.
3.  Implement tests.
