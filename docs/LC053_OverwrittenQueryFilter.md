---
layout: default
title: "LC053: Global query filter silently replaced by another HasQueryFilter"
description: "LC053 flags a second unnamed EF Core HasQueryFilter on one entity, which replaces the first and silently drops a tenant or soft-delete filter."
---

# LC053: Global query filter silently replaced by another HasQueryFilter

## In Plain Terms

Two people each put a sign on the same door. The second sign goes on top of the first, so nobody reads the first one any more, and nobody told the first person.

## Goal

Detect an entity type that gets more than one unnamed `HasQueryFilter(...)` call, and unnamed filters mixed with named ones (EF Core 10+).

## The Problem

`HasQueryFilter` does not add a filter. For unnamed filters it sets *the* filter, so a later call replaces the earlier one without any warning:

```csharp
// TenantConfiguration.cs
builder.HasQueryFilter(b => b.TenantId == _tenantId);

// SoftDeleteConfiguration.cs, applied later
builder.HasQueryFilter(b => !b.IsDeleted); // Violation: the tenant filter is gone
```

Queries then return rows from every tenant. Nothing fails at startup and tests that use a single tenant still pass. This is the most common way global query filters are lost, because tenant and soft-delete filters tend to live in different configuration classes or base-class helpers.

EF Core 10 adds named filters (`HasQueryFilter("Tenant", ...)`). Mixing an unnamed filter with named ones on the same entity type throws when the model is built, so LC053 reports that too.

## The Fix

Combine the conditions in one filter:

```csharp
builder.HasQueryFilter(b => b.TenantId == _tenantId && !b.IsDeleted);
```

On EF Core 10+, give each filter a name so they apply together and can be disabled one at a time with `IgnoreQueryFilters(["SoftDelete"])`:

```csharp
builder.HasQueryFilter("Tenant", b => b.TenantId == _tenantId);
builder.HasQueryFilter("SoftDelete", b => !b.IsDeleted);
```

## Analyzer Logic

### ID: `LC053`
### Category: `Security`
### Severity: `Warning`

1. Find calls to EF Core's generic `EntityTypeBuilder<TEntity>.HasQueryFilter`, and group them by entity type.
2. Report each unnamed filter when another unnamed filter for the same entity type can also run. Calls in different branches of the same `if`, `?:` or `switch` cannot both run and are not counted.
3. Report each unnamed filter on an entity type that also has named filters.
4. Conflicts inside one method are reported as you type. Conflicts between methods or configuration classes need the whole project, so they are reported when the project builds.

## When it stays quiet (non-goals)

- One filter per entity type, or only named filters.
- `HasQueryFilter(null)`, which clears the filter on purpose.
- Filters in mutually exclusive branches (`if` / `else`, `?:`, `switch` sections and arms).
- The non-generic `EntityTypeBuilder.HasQueryFilter(LambdaExpression)`, usually called in a loop over `modelBuilder.Model.GetEntityTypes()`. The rule cannot tell which entity type each call configures.
- `HasQueryFilter` methods that are not EF Core's.

## Code Fix

When exactly two unnamed filters conflict and each is its own `builder.HasQueryFilter(x => ...)` statement in the same block, the fix removes the earlier statement and adds its condition to the later filter with `&&`:

```csharp
// Before
modelBuilder.Entity<Blog>().HasQueryFilter(b => b.TenantId == _tenantId);
modelBuilder.Entity<Blog>().HasQueryFilter(b => !b.IsDeleted);

// After
modelBuilder.Entity<Blog>().HasQueryFilter(b => b.TenantId == _tenantId && !b.IsDeleted);
```

The earlier lambda parameter is renamed to match the later one, and `||` or `?:` conditions are parenthesized. The fix is not offered when the filters are in different methods or classes, when there are three or more, when a call is chained (`.HasQueryFilter(...).HasKey(...)`), when the earlier receiver could have side effects, or when renaming the parameter would change what a name refers to. Naming the filters is left to you, because it needs EF Core 10 and filter names only you can choose.

## Test Cases

### Violations

```csharp
modelBuilder.Entity<Blog>().HasQueryFilter(b => b.TenantId == _tenantId);
modelBuilder.Entity<Blog>().HasQueryFilter(b => !b.IsDeleted);

modelBuilder.Entity<Blog>().HasQueryFilter("Tenant", b => b.TenantId == _tenantId);
modelBuilder.Entity<Blog>().HasQueryFilter(b => !b.IsDeleted); // unnamed mixed with named
```

### Valid

```csharp
modelBuilder.Entity<Blog>().HasQueryFilter(b => b.TenantId == _tenantId && !b.IsDeleted);

modelBuilder.Entity<Blog>().HasQueryFilter("Tenant", b => b.TenantId == _tenantId);
modelBuilder.Entity<Blog>().HasQueryFilter("SoftDelete", b => !b.IsDeleted);
```
