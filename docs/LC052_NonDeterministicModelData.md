---
layout: default
title: "LC052: Model data uses a value that changes on every run"
description: "LC052 flags HasData and HasDefaultValue with DateTime.Now or Guid.NewGuid(), which change the EF Core model every run and make EF Core 9 Migrate() throw."
---

# LC052: Model data uses a value that changes on every run

## In Plain Terms

You write "today's date" on a form that is supposed to be a master copy. Every time someone checks the copy against the original, they differ, so everyone thinks the form was changed.

## Goal

Detect `DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow`, `Guid.NewGuid()`, and `Guid.CreateVersion7()` inside `HasData(...)` seed data and `HasDefaultValue(...)`.

## The Problem

Seed data and column defaults are part of the EF Core model. The migration snapshot stores their values. When a value comes from the clock or a new GUID, every run builds a different model than the last migration recorded:

```csharp
// Violation: a new timestamp and key on every run.
modelBuilder.Entity<Blog>().HasData(new Blog { Id = 1, Key = Guid.NewGuid(), CreatedAt = DateTime.UtcNow });

// Violation: the column default is frozen at whenever the model was built.
modelBuilder.Entity<Blog>().Property(b => b.CreatedAt).HasDefaultValue(DateTime.UtcNow);
```

- `dotnet ef migrations add` produces a new migration that updates the seed rows each time, even with no real change.
- Since EF Core 9, `Migrate()` and `dotnet ef database update` throw `PendingModelChangesWarning` because the model never matches the snapshot. The [EF Core 9 breaking changes](https://learn.microsoft.com/ef/core/what-is-new/ef-core-9.0/breaking-changes#pending-model-changes) name these calls as the usual cause.
- A `HasDefaultValue(DateTime.UtcNow)` default is not "now" at insert time. It is the time the migration was generated.

## The Fix

Seed fixed literals, and let the database supply "now" for column defaults:

```csharp
modelBuilder.Entity<Blog>().HasData(new Blog
{
    Id = 1,
    Key = new Guid("8c1d2f6e-7a55-4c43-9a2a-1f8b3e6d9c01"),
    CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
});

modelBuilder.Entity<Blog>().Property(b => b.CreatedAt).HasDefaultValueSql("GETUTCDATE()"); // SQL Server
```

For data that really must be generated at runtime, use `UseSeeding` / `UseAsyncSeeding` (EF Core 9+) instead of `HasData`.

## Analyzer Logic

### ID: `LC052`
### Category: `Reliability`
### Severity: `Warning`

1. Find calls to EF Core's `HasData` (entity and owned-type builders) and `HasDefaultValue`.
2. Look through every argument, including object initializers, anonymous objects, and arrays.
3. Report each clock or new-GUID value found, directly or through up to three locals or `static readonly` fields whose only value contains one.

## When it stays quiet (non-goals)

- Fixed values: `new DateTime(...)`, `new Guid("...")`, literals, and constants.
- `HasDefaultValueSql(...)` and `HasComputedColumnSql(...)`, which run in the database.
- Locals that are written again after their declaration, instance fields, and mutable static fields. The rule cannot prove what they hold when the model is built.
- Values returned from helper methods.
- `HasData` methods that are not EF Core's.

## Why There Is No Code Fix

No automatic fix. A seed value needs a fixed literal that only the author can choose, and a database default needs provider-specific SQL (`GETUTCDATE()` on SQL Server, `now()` on PostgreSQL, `CURRENT_TIMESTAMP` on SQLite).

## Test Cases

### Violations

```csharp
modelBuilder.Entity<Blog>().HasData(new Blog { Id = 1, CreatedAt = DateTime.Now });
modelBuilder.Entity<Blog>().HasData(new { Id = 1, Key = Guid.NewGuid() });
modelBuilder.Entity<Blog>().Property(b => b.CreatedAt).HasDefaultValue(DateTime.UtcNow);
```

### Valid

```csharp
modelBuilder.Entity<Blog>().HasData(new Blog { Id = 1, CreatedAt = new DateTime(2024, 1, 1) });
modelBuilder.Entity<Blog>().Property(b => b.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
```
