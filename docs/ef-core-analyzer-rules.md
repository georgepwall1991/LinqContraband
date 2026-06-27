---
layout: default
title: EF Core Analyzer Rules
description: A practical guide to LinqContraband's 45 EF Core analyzer rules for query performance, loading, tracking, async execution, raw SQL safety, and CI policy.
permalink: /ef-core-analyzer-rules/
body_class: page-analyzer-rules
---

# EF Core Analyzer Rules

LinqContraband provides 45 EF Core analyzer rules for teams that want repeatable query review in the IDE and CI. The
rules cover LINQ query shape, materialization, loading, async execution, tracking, bulk operations, schema modeling, and
raw SQL safety.

Install the official analyzer package:

```bash
dotnet add package LinqContraband
```

## Rule Families

Use these groups to decide which EF Core diagnostics are most useful for your project before diving into the full rule
catalog.

| Rule family | Use it when you want to catch | Starting rules |
| --- | --- | --- |
| Query shape and translation | Local methods, unstable ordering, non-translatable overloads, and query shapes that can fall out of SQL translation. | [LC001: local method](/LinqContraband/LC001_LocalMethod.html), [LC015: missing OrderBy](/LinqContraband/LC015_MissingOrderBy.html), [LC024: non-translatable GroupBy](/LinqContraband/LC024_GroupByNonTranslatable.html) |
| Materialization and projection | Early `ToList`, whole-entity fetches, unbounded result sets, and scalar reads that should project in SQL. | [LC002: premature materialization](/LinqContraband/LC002_PrematureMaterialization.html), [LC017: whole entity projection](/LinqContraband/LC017_WholeEntityProjection.html), [LC041: scalar projection](/LinqContraband/LC041_SingleEntityScalarProjection.html) |
| Loading and includes | Missing includes, cartesian explosion, excessive eager loading, deep include chains, and untagged complex queries. | [LC045: missing include](/LinqContraband/LC045_MissingInclude.html), [LC006: cartesian explosion](/LinqContraband/LC006_CartesianExplosion.html), [EF Core Include analyzer](/LinqContraband/ef-core-include-analyzer/) |
| Execution and async | Database work inside loops, synchronous EF Core calls in async paths, missing cancellation tokens, and async-stream buffering. | [LC007: database execution inside loop](/LinqContraband/LC007_NPlusOneLooper.html), [LC008: sync-over-async](/LinqContraband/LC008_SyncBlocker.html), [LC026: missing cancellation token](/LinqContraband/LC026_MissingCancellationToken.html) |
| Tracking and context lifetime | Missing `AsNoTracking`, no-tracking writes, mixed tracking modes, repeated `SaveChanges`, and DbContext lifetime mistakes. | [LC009: missing AsNoTracking](/LinqContraband/LC009_MissingAsNoTracking.html), [LC044: no-tracking modification](/LinqContraband/LC044_AsNoTrackingThenModifySilentWrite.html), [EF Core AsNoTracking analyzer](/LinqContraband/ef-core-asnotracking-analyzer/) |
| Bulk operations and modeling | Set-based write opportunities, unbounded bulk updates or deletes, missing keys, and missing explicit foreign keys. | [LC032: ExecuteUpdate](/LinqContraband/LC032_ExecuteUpdateForBulkUpdates.html), [LC035: missing Where before bulk execute](/LinqContraband/LC035_MissingWhereBeforeExecuteDeleteUpdate.html), [EF Core ExecuteUpdate analyzer](/LinqContraband/ef-core-executeupdate-analyzer/) |
| Raw SQL and security | Interpolated raw SQL, constructed SQL strings, unsafe command SQL, and query-filter bypasses. | [LC018: interpolated raw SQL](/LinqContraband/LC018_AvoidFromSqlRawWithInterpolation.html), [LC034: interpolated command SQL](/LinqContraband/LC034_AvoidExecuteSqlRawWithInterpolation.html), [LC021: IgnoreQueryFilters](/LinqContraband/LC021_AvoidIgnoreQueryFilters.html) |

## Rules To Enable First

For a low-noise rollout, start with the rules that usually represent clear production risk:

```ini
[*.cs]

# Repeated database work and missing related data
dotnet_diagnostic.LC007.severity = error
dotnet_diagnostic.LC045.severity = warning

# Raw SQL and query-filter bypasses
dotnet_diagnostic.LC018.severity = error
dotnet_diagnostic.LC034.severity = error
dotnet_diagnostic.LC037.severity = error
dotnet_diagnostic.LC021.severity = warning

# Expensive query shapes
dotnet_diagnostic.LC002.severity = warning
dotnet_diagnostic.LC031.severity = warning
```

Keep broader design guidance as warnings until the team has reviewed existing findings. Promote a rule to `error` only
when it represents project policy and developers have a documented exception path.

## Where Each Rule Fits

- Use the [EF Core query performance checklist](/LinqContraband/ef-core-query-performance-checklist/) when you need a
  reviewer-friendly pull-request aid.
- Use the [EF Core Include analyzer guide](/LinqContraband/ef-core-include-analyzer/) when missing related data,
  cartesian explosion, or over-eager loading are the main concern.
- Use the [EF Core N+1 query detector guide](/LinqContraband/ef-core-n-plus-one-query-detector/) when repeated database
  calls or loading strategy are the main concern.
- Use the [EF Core raw SQL injection analyzer guide](/LinqContraband/ef-core-raw-sql-injection-analyzer/) when raw SQL,
  command SQL, or query-filter bypasses need stronger review.
- Use the [EF Core AsNoTracking analyzer guide](/LinqContraband/ef-core-asnotracking-analyzer/) when read-only query
  tracking, mixed tracking modes, or no-tracking write paths are the main concern.
- Use the [EF Core ExecuteUpdate analyzer guide](/LinqContraband/ef-core-executeupdate-analyzer/) when tracked bulk
  loops, `RemoveRange` deletes, or unfiltered set-based writes are the main concern.
- Use the [EF Core query analyzer CI guide](/LinqContraband/ef-core-query-analyzer-ci/) when selected rules should warn
  or fail pull requests.
- Use the [full rule catalog](/LinqContraband/rule-catalog.html) for every rule's severity, category, sample folder, and
  code-fix status.

## Official Links

- Canonical repository: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Official NuGet package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Documentation hub: [georgepwall1991.github.io/LinqContraband](https://georgepwall1991.github.io/LinqContraband/)
- Safe install guidance: [Official LinqContraband downloads and authenticity](/LinqContraband/security-and-authenticity/)
