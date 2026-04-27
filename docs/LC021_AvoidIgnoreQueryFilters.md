# Spec: LC021 - Avoid IgnoreQueryFilters

## Goal
Detect usage of EF Core's `IgnoreQueryFilters()` on an `IQueryable`. Global query filters are often used for critical cross-cutting concerns like multi-tenancy, soft-delete, or security. Bypassing them can lead to data leaks or incorrect business logic.

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
1.  **Target Method**: Intercept invocations named `IgnoreQueryFilters`.
2.  **EF Core Boundary**: Require `Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters`.
3.  **Query Boundary**: Require an `IQueryable` receiver so unrelated instance methods or custom `IEnumerable` helpers with the same name stay silent.

## Test Cases

### Violations
```csharp
db.Users.IgnoreQueryFilters().Where(x => x.Active);
```

### Valid
```csharp
db.Users.Where(x => x.Active);
```

LC021 intentionally stays quiet for lookalikes that are not the EF Core extension method:

```csharp
// Custom IQueryable helper outside Microsoft.EntityFrameworkCore is not LC021.
CustomQueryExtensions.IgnoreQueryFilters(query);

// Instance or IEnumerable helpers with the same name are not LC021.
auditQuery.IgnoreQueryFilters();
values.IgnoreQueryFilters();
```

## Shipped Behavior

LC021 reports EF Core `IgnoreQueryFilters()` calls so filter bypasses are visible during review. The fixer removes the call when the bypass is accidental; keep the diagnostic suppressed or documented only when the query intentionally crosses tenant, soft-delete, or security-filter boundaries.

Intentional bypasses should be local and auditable:

```csharp
#pragma warning disable LC021
var reviewedUser = db.Users
    .IgnoreQueryFilters()
    .TagWith("Audited tenant-review bypass")
    .Where(user => user.Id == userId)
    .ToList();
#pragma warning restore LC021
```

Prefer a narrow pragma around the reviewed query over disabling LC021 for a whole file or project. If a query needs to bypass filters regularly, prefer a named repository/service method that documents the business reason and applies explicit replacement filters.

The fixer is intentionally narrow: it removes only the `.IgnoreQueryFilters()` call and preserves the rest of the query chain. Do not apply the fixer when the bypass is part of an approved administrative, tenant-review, soft-delete restore, or security-audit workflow.
