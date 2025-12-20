# Spec: LC021 - Avoid IgnoreQueryFilters

## Goal
Detect usage of `IgnoreQueryFilters()` on an `IQueryable`. Global query filters are often used for critical cross-cutting concerns like multi-tenancy, soft-delete, or security. Bypassing them can lead to data leaks or incorrect business logic.

## The Problem
Global query filters are applied automatically to all queries for a given entity type. `IgnoreQueryFilters()` disables them for the current query. While sometimes necessary (e.g., for administrative tools or restoring soft-deleted items), it is often used accidentally or without full understanding of the security implications.

### Example Violation
```csharp
// Violation: Might bypass multi-tenancy or soft-delete filters
var allUsers = db.Users.IgnoreQueryFilters().ToList();
```

### The Fix
Ensure that bypassing global filters is intentional and documented. If possible, use explicit filtering instead of relying on global filters if you need to access "filtered out" data frequently.

## Analyzer Logic

### ID: `LC021`
### Category: `Security` (or `Reliability`)
### Severity: `Warning`

### Algorithm
1.  **Target Method**: Intercept invocations of `IgnoreQueryFilters`.
2.  **Type Check**: Ensure the method is the EF Core extension method for `IQueryable`.

## Test Cases

### Violations
```csharp
db.Users.IgnoreQueryFilters().Where(x => x.Active);
```

### Valid
```csharp
db.Users.Where(x => x.Active);
```

## Implementation Plan
1.  Create `LC021_AvoidIgnoreQueryFilters` directory.
2.  Implement `AvoidIgnoreQueryFiltersAnalyzer`.
3.  Implement tests.
