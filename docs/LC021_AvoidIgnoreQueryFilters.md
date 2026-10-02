---
layout: default
title: "LC021: Avoid IgnoreQueryFilters"
description: "LC021 flags IgnoreQueryFilters() in EF Core queries, which bypasses global filters for soft delete, multi-tenancy or security and can leak data."
---

# LC021: Avoid IgnoreQueryFilters

## In Plain Terms

Imagine a high-security building where every door has a lock. `IgnoreQueryFilters`
is like a skeleton key that opens every single door at once. It's powerful, but if you use it by accident, you might
end up somewhere you're not supposed to be.

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

db.Users.IgnoreQueryFilters(new[] { "TenantFilter" }).ToList();

EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Users).ToList();
```

### Valid
```csharp
db.Users.Where(x => x.Active);

// Maintenance code that reads soft-deleted or archived rows on purpose.
db.Posts.IgnoreQueryFilters().Where(p => p.IsDeleted).ToList();
db.Posts.IgnoreQueryFilters().Where(p => p.Id == id && p.DeletedAt != null).ToList();
```

LC021 stays quiet when a `Where` in the same query chain (before or after `IgnoreQueryFilters()`) selects deleted or archived rows: a `bool` property whose name contains `Delete` or `Archiv` (`p.Deleted`, `p.IsDeleted`, `p.IsDeleted == true`, `p.IsArchived == true`), a nullable one compared to `null` (`p.DeletedAt != null`, `p.DeletedAt.HasValue`, `p.DeletedBy != null`), or any of these as one side of `&&`. The property must belong to the `Where` lambda's own row (a captured `options.IncludeDeleted` does not count), and negated names such as `IsNotDeleted` or `Undeleted` do not count. This applies only to the parameterless `IgnoreQueryFilters()`: a named overload such as `IgnoreQueryFilters(["Tenant"])` keeps reporting even with a deleted-rows `Where`, because the named filter may guard something else. Without the call such a query is always empty, so removing it, as the fixer would, breaks the restore or purge code. A predicate that excludes deleted rows (`!p.IsDeleted`, `p.DeletedAt == null`), an `||`, or a `Where` applied in a later statement keeps the report, because the call may still be there to cross a tenant or security filter.

LC021 intentionally stays quiet for lookalikes that are not the EF Core extension method:

```csharp
// Custom IQueryable helper outside Microsoft.EntityFrameworkCore is not LC021.
CustomQueryExtensions.IgnoreQueryFilters(query);

// Instance or IEnumerable helpers with the same name are not LC021.
auditQuery.IgnoreQueryFilters();
values.IgnoreQueryFilters();
```

## Shipped Behavior

LC021 reports EF Core `IgnoreQueryFilters()` calls so filter bypasses are visible during review. That includes EF Core's named-filter overload, because `IgnoreQueryFilters(filterKeys)` still disables the named filters passed to it. EF Core 10 code such as `IgnoreQueryFilters(["SoftDelete"])` or `IgnoreQueryFilters(new[] { "SoftDelete" })` keeps reporting: a filter's name is only a string and says nothing reliable about what the filter guards (a filter named `SoftDelete` can also scope by tenant), so the analyzer does not guess from it. Removing the named call is still a well-defined fix: it turns the named filters back on and leaves the others as they were. The fixer removes the call when the bypass is accidental, including static extension-method syntax such as `EntityFrameworkQueryableExtensions.IgnoreQueryFilters(query)` and named-filter syntax such as `query.IgnoreQueryFilters(filterKeys)`; keep the diagnostic suppressed or documented only when the query intentionally crosses tenant, soft-delete, or security-filter boundaries.

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

For approved service-level bypasses, a targeted `SuppressMessage` attribute with a concrete justification is also supported:

```csharp
[SuppressMessage("Security", "LC021", Justification = "Reviewed tenant-admin bypass.")]
public List<User> GetAllTenantsForAdminReview()
{
    return db.Users
        .IgnoreQueryFilters()
        .Where(user => user.IsActive)
        .ToList();
}
```

Method-level and type-level `SuppressMessage` suppressions are supported for reviewed bypass services. Repository-wide analyzer config such as `dotnet_diagnostic.LC021.severity = none` is also honored by Roslyn, but treat it as a project policy decision rather than a local exception. Generated code is excluded from LC021 analysis.

The fixer is intentionally narrow: it removes only the `.IgnoreQueryFilters()` call, or the equivalent static extension wrapper, and preserves the rest of the query chain. For named-filter overloads, extension syntax keeps the original query receiver (`query.IgnoreQueryFilters(filterKeys)` becomes `query`) while static syntax keeps the explicit source query argument (`EntityFrameworkQueryableExtensions.IgnoreQueryFilters(query, filterKeys)` and `EntityFrameworkQueryableExtensions.IgnoreQueryFilters(filterKeys: filters, source: query)` both become `query`). Do not apply the fixer when the bypass is part of an approved administrative, tenant-review, soft-delete restore, or security-audit workflow.
