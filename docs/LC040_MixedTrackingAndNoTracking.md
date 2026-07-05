---
layout: default
title: "Spec: LC040 - Mixed Tracking and No-Tracking"
---

# Spec: LC040 - Mixed Tracking and No-Tracking

## Goal
Detect methods that mix tracked and no-tracking materialization from the same `DbContext`.

## The Problem
Switching between tracked and `AsNoTracking()` queries in one scope is easy to miss and can make later update behavior inconsistent.

### Example Violation
```csharp
var trackedUsers = db.Users.ToList();
var noTrackingUsers = db.Users.AsNoTracking().ToList();
```

## Analyzer Logic

### ID: `LC040`
### Category: `Reliability`
### Severity: `Info`

### Notes
This advisory reports only when the query provenance and materialization mode are both provable.

Only EF Core `EntityFrameworkQueryableExtensions.AsNoTracking`, `AsNoTrackingWithIdentityResolution`, and `AsTracking` calls are treated as tracking-mode markers. Custom extension methods with the same names are followed as ordinary query-chain calls and do not create mixed-mode evidence by themselves.

Straight-line local query aliases are resolved at the materialization point. A local reassigned from tracked to no-tracking queries on the same context can report, while a reassignment from a different context or inside conditional control flow stays quiet.

Mutually exclusive `if`/`else` branches, `switch` sections, and ternary (`cond ? a : b`) arms are not treated as mixed tracking evidence by themselves. Later materialization still compares against every reachable earlier tracking mode so split branches followed by shared work can be reported when one path really mixes modes.

Transparent EF query options such as `AsSplitQuery()` and `TagWith(...)` do not change the tracking mode. LC040 follows through those calls and still reports when the same context materializes one tracked result and one no-tracking result.

`DbContext.Set<TEntity>()` is treated as tracked query evidence in the same way as a `DbSet<TEntity>` property, so mixing `db.Set<User>().ToList()` with `db.Users.AsNoTracking().ToList()` in the same method reports when both calls resolve to the same context.

An explicit transaction does not make mixed tracking modes safer by itself. Transactions coordinate database writes; they do not change whether EF tracks the materialized entities. If a transactional workflow needs both read-only and write paths, split the workflow into clearly named scopes or contexts, or document why the mixed mode is intentional.

## No automatic fixer

LC040 is manual-only because the correct resolution depends on intent:

- choose fully tracked queries when the method will modify or save entities;
- choose fully no-tracking queries for read-only work;
- split the workflow across separate methods, contexts, or scopes when one operation genuinely needs both modes.

The analyzer deliberately stays quiet for different context instances and for mutually exclusive branches where one execution path does not actually mix modes.
