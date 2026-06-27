---
layout: default
title: EF Core Query Performance Checklist
description: A practical EF Core query performance checklist for reviewing LINQ queries, N+1 risks, raw SQL safety, tracking modes, and CI enforcement with LinqContraband.
permalink: /ef-core-query-performance-checklist/
body_class: page-performance-checklist
---

# EF Core Query Performance Checklist

Use this EF Core query performance checklist when reviewing pull requests, tightening team standards, or adding
compile-time query analysis to CI. It maps common review questions to LinqContraband rules so teams can move repeatable
feedback out of human memory and into analyzer diagnostics.

Install the official analyzer package:

```bash
dotnet add package LinqContraband
```

## Pull Request Checklist

| Check | Why it matters | LinqContraband signal |
| --- | --- | --- |
| Avoid database calls inside loops. | Loop execution can turn one request into many database roundtrips. | [LC007: database execution inside loop](/LinqContraband/LC007_NPlusOneLooper.html) |
| Materialize only after filtering, ordering, and projection. | Early `ToList`, `AsEnumerable`, or `ToArray` can move work from SQL into memory. | [LC002: premature materialization](/LinqContraband/LC002_PrematureMaterialization.html) |
| Use projection when only a few fields are needed. | Loading whole entities increases network, memory, and tracking cost. | [LC017: whole entity projection](/LinqContraband/LC017_WholeEntityProjection.html), [LC041: single entity scalar projection](/LinqContraband/LC041_SingleEntityScalarProjection.html) |
| Make related data loading explicit. | Missing includes can produce null navigation data, lazy-loading churn, or hidden N+1 behaviour. | [LC045: missing include](/LinqContraband/LC045_MissingInclude.html) |
| Keep eager loading bounded. | Overusing `Include` can create cartesian explosion or very wide result graphs. | [LC006: cartesian explosion](/LinqContraband/LC006_CartesianExplosion.html), [LC038: excessive eager loading](/LinqContraband/LC038_ExcessiveEagerLoading.html) |
| Prefer async EF Core APIs in async methods. | Synchronous EF Core calls in async paths block request threads. | [LC008: sync-over-async](/LinqContraband/LC008_SyncBlocker.html) |
| Pass cancellation tokens through async query APIs. | Long-running queries should respect request cancellation and shutdown paths. | [LC026: missing cancellation token](/LinqContraband/LC026_MissingCancellationToken.html) |
| Use read-only tracking intentionally. | Tracking every read increases memory and can create confusing mixed-mode behaviour. | [LC009: missing AsNoTracking](/LinqContraband/LC009_MissingAsNoTracking.html), [LC040: mixed tracking modes](/LinqContraband/LC040_MixedTrackingAndNoTracking.html) |
| Keep raw SQL parameterized. | Interpolation and string construction can turn EF Core raw SQL into injection risk. | [LC018: interpolated raw SQL](/LinqContraband/LC018_AvoidFromSqlRawWithInterpolation.html), [LC034: interpolated command SQL](/LinqContraband/LC034_AvoidExecuteSqlRawWithInterpolation.html), [LC037: constructed raw SQL strings](/LinqContraband/LC037_RawSqlStringConstruction.html) |
| Review global filter bypasses. | `IgnoreQueryFilters` can skip tenant, soft-delete, or security boundaries. | [LC021: IgnoreQueryFilters](/LinqContraband/LC021_AvoidIgnoreQueryFilters.html) |
| Bound destructive set-based writes. | Bulk delete/update without a filter can affect more rows than intended. | [LC035: missing Where before bulk execute](/LinqContraband/LC035_MissingWhereBeforeExecuteDeleteUpdate.html) |
| Prefer reviewed set-based writes for large batches. | Tracked loops and `RemoveRange` can load rows that EF Core can update or delete directly in SQL. | [LC032: ExecuteUpdate](/LinqContraband/LC032_ExecuteUpdateForBulkUpdates.html), [LC012: ExecuteDelete over RemoveRange](/LinqContraband/LC012_OptimizeRemoveRange.html) |
| Tag complex queries before they reach production. | Query tags make expensive SQL easier to trace in database telemetry. | [LC042: missing query tags](/LinqContraband/LC042_MissingQueryTags.html) |

## Quick Severity Policy

Start with analyzer warnings, then promote a focused rule set to errors once the team has cleared existing findings:

```ini
[*.cs]

# Expensive or risky query shapes
dotnet_diagnostic.LC002.severity = warning
dotnet_diagnostic.LC007.severity = error
dotnet_diagnostic.LC045.severity = warning

# Raw SQL and security-sensitive paths
dotnet_diagnostic.LC018.severity = error
dotnet_diagnostic.LC034.severity = error
dotnet_diagnostic.LC037.severity = error
dotnet_diagnostic.LC021.severity = warning
```

Use project-wide `error` severities for rules that represent team policy. Use narrow pragmas or `SuppressMessage`
attributes only when a reviewer has accepted a specific exception.

## How To Use This Checklist

- Link it from pull request templates, engineering handbooks, and onboarding docs.
- Pair it with the [EF Core analyzer rules guide](/LinqContraband/ef-core-analyzer-rules/) when a team needs the
  broader diagnostic map before choosing severities.
- Pair it with the [EF Core query analyzer CI guide](/LinqContraband/ef-core-query-analyzer-ci/) so the highest-risk
  checklist items become automated build feedback.
- Send focused topics to the [EF Core Include analyzer guide](/LinqContraband/ef-core-include-analyzer/), the
  [EF Core N+1 query detector guide](/LinqContraband/ef-core-n-plus-one-query-detector/), and the
  [EF Core raw SQL injection analyzer guide](/LinqContraband/ef-core-raw-sql-injection-analyzer/) when reviewers need
  deeper examples.
- Use the [EF Core AsNoTracking analyzer guide](/LinqContraband/ef-core-asnotracking-analyzer/) when read-only query
  tracking, mixed tracking modes, or silent no-tracking writes need focused guidance.
- Use the [EF Core ExecuteUpdate analyzer guide](/LinqContraband/ef-core-executeupdate-analyzer/) when bulk update,
  bulk delete, or missing `Where` before set-based writes need focused guidance.
- Use the [full rule catalog](/LinqContraband/rule-catalog.html) to tune the exact severity policy for your application.

## Official Links

- Canonical repository: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Official NuGet package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Documentation hub: [georgepwall1991.github.io/LinqContraband](https://georgepwall1991.github.io/LinqContraband/)
- Safe install guidance: [Official LinqContraband downloads and authenticity](/LinqContraband/security-and-authenticity/)
