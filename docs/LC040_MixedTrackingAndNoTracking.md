---
layout: default
title: "LC040: Mixed Tracking and No-Tracking"
description: "LC040 flags methods that mix tracked and AsNoTracking() queries on the same DbContext, which makes later update behavior hard to predict."
---

# LC040: Mixed Tracking and No-Tracking

## In Plain Terms

Imagine half your soccer team is wearing jerseys with numbers and the other
half is invisible. The coach cannot tell who is on the field anymore.

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
This advisory reports only when the query provenance and materialization mode are both provable. It only counts reads that materialize the entity type of the `DbSet` they start from; a query a helper reshapes into DTOs or scalars, such as AutoMapper's `ProjectTo<Dto>()`, is not tracked either way.

Only EF Core `EntityFrameworkQueryableExtensions.AsNoTracking`, `AsNoTrackingWithIdentityResolution`, and `AsTracking` calls are treated as tracking-mode markers. Custom extension methods with the same names are followed as ordinary query-chain calls and do not create mixed-mode evidence by themselves.

Straight-line local query aliases are resolved at the materialization point. A local reassigned from tracked to no-tracking queries on the same context can report, while a reassignment from a different context or inside conditional control flow stays quiet. A query composed in place (`query = query.Where(...)`, `query = query.AsNoTracking()`) resolves through each step back to its source, and takes its tracking mode from the last `AsNoTracking()` or `AsTracking()` applied to it.

Mutually exclusive `if`/`else` branches, `switch` sections, and ternary (`cond ? a : b`) arms are not treated as mixed tracking evidence by themselves. Neither is a read in an `if` branch that always ends in `return` or `throw` together with a read after that `if`, as in `if (track) return query.ToList(); return query.AsNoTracking().ToList();`. Later materialization still compares against every reachable earlier tracking mode so split branches followed by shared work can be reported when one path really mixes modes.

Transparent EF query options such as `AsSplitQuery()` and `TagWith(...)` do not change the tracking mode. LC040 follows through those calls and still reports when the same context materializes one tracked result and one no-tracking result.

`DbContext.Set<TEntity>()` is treated as tracked query evidence in the same way as a `DbSet<TEntity>` property, so mixing `db.Set<User>().ToList()` with `db.Users.AsNoTracking().ToList()` in the same method reports when both calls resolve to the same context.

An explicit transaction does not make mixed tracking modes safer by itself. Transactions coordinate database writes; they do not change whether EF tracks the materialized entities. If a transactional workflow needs both read-only and write paths, split the workflow into clearly named scopes or contexts, or document why the mixed mode is intentional.

## No automatic fixer

LC040 is manual-only because the correct resolution depends on intent:

- choose fully tracked queries when the method will modify or save entities;
- choose fully no-tracking queries for read-only work;
- split the workflow across separate methods, contexts, or scopes when one operation genuinely needs both modes.

The analyzer deliberately stays quiet for different context instances and for mutually exclusive branches where one execution path does not actually mix modes.
