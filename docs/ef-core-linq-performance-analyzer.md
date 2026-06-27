---
layout: default
title: EF Core LINQ Performance Analyzer
description: How LinqContraband catches Entity Framework Core query performance, reliability, and security issues at compile time.
permalink: /ef-core-linq-performance-analyzer/
---

# EF Core LINQ Performance Analyzer

LinqContraband is a Roslyn analyzer for teams that want Entity Framework Core query feedback during development instead
of after a slow production endpoint appears. It runs at compile time and reviews LINQ query shapes for performance,
reliability, and security issues.

For a reviewer-friendly summary, use the [EF Core query performance checklist](/LinqContraband/ef-core-query-performance-checklist/).
For a rules-by-domain view, see the [EF Core analyzer rules guide](/LinqContraband/ef-core-analyzer-rules/).

Install the official NuGet package:

```bash
dotnet add package LinqContraband
```

## Problems It Helps Catch

- N+1 query loops and missing includes
- Premature materialization with `ToList()`, `ToArray()`, `AsEnumerable()`, and related operators
- Client-side evaluation risk from local methods and untranslatable query expressions
- Sync-over-async EF Core calls inside async methods
- Missing `AsNoTracking()` in read-only paths
- Unsafe raw SQL interpolation and constructed SQL strings
- Silent no-tracking writes and mixed tracking modes
- Bulk update/delete operations without an explicit filter

For Include and eager-loading review, see the [EF Core Include analyzer guide](/LinqContraband/ef-core-include-analyzer/).
For a focused walkthrough, see the [EF Core N+1 query detector guide](/LinqContraband/ef-core-n-plus-one-query-detector/).
For security-sensitive SQL usage, see the [EF Core raw SQL injection analyzer guide](/LinqContraband/ef-core-raw-sql-injection-analyzer/).
For tracking-mode mistakes, see the [EF Core AsNoTracking analyzer guide](/LinqContraband/ef-core-asnotracking-analyzer/).
For set-based write review, see the [EF Core ExecuteUpdate analyzer guide](/LinqContraband/ef-core-executeupdate-analyzer/).
For pull-request enforcement, see the [EF Core query analyzer CI guide](/LinqContraband/ef-core-query-analyzer-ci/).

## Why Compile-Time Analysis Helps

EF Core problems often hide behind ordinary-looking LINQ. A query can compile, pass local testing, and still create a
large SQL result, many roundtrips, or a surprising tracking side effect. LinqContraband brings those issues into the IDE
and CI pipeline, where they are cheaper to fix.

The analyzer has no runtime cost. It ships as a NuGet analyzer package and works with common .NET development
environments including Visual Studio, Rider, VS Code, and CI builds.

## Browse the Rules

The full catalog contains 45 rules grouped by domain:

- [Rule catalog](/LinqContraband/rule-catalog.html)
- [EF Core analyzer rules guide](/LinqContraband/ef-core-analyzer-rules/)
- [EF Core query performance checklist](/LinqContraband/ef-core-query-performance-checklist/)
- [EF Core Include analyzer](/LinqContraband/ef-core-include-analyzer/)
- [EF Core N+1 query detector](/LinqContraband/ef-core-n-plus-one-query-detector/)
- [EF Core raw SQL injection analyzer](/LinqContraband/ef-core-raw-sql-injection-analyzer/)
- [EF Core AsNoTracking analyzer](/LinqContraband/ef-core-asnotracking-analyzer/)
- [EF Core ExecuteUpdate analyzer](/LinqContraband/ef-core-executeupdate-analyzer/)
- [EF Core query analyzer for CI](/LinqContraband/ef-core-query-analyzer-ci/)
- [LC007: N+1 query loops](/LinqContraband/LC007_NPlusOneLooper.html)
- [LC045: missing include](/LinqContraband/LC045_MissingInclude.html)
- [LC002: premature materialization](/LinqContraband/LC002_PrematureMaterialization.html)
- [LC018: interpolated raw SQL](/LinqContraband/LC018_AvoidFromSqlRawWithInterpolation.html)
- [LC037: constructed raw SQL strings](/LinqContraband/LC037_RawSqlStringConstruction.html)

## Official Links

- Canonical repository: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Official package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Maintainer: [George Wall](https://www.georgewall.uk/)
