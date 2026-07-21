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
- Projection waste from whole-entity loads, scalar over-fetching, and redundant `Select(x => x)` calls
- Client-side evaluation risk from local methods and untranslatable query expressions
- Query translation failures from `StringComparison`, column case conversion, and non-translatable `GroupBy` projections
- Sync-over-async EF Core calls inside async methods
- Missing cancellation tokens on async EF Core queries and saves
- Async stream buffering before a single `await foreach`
- Concurrent async EF Core operations on one `DbContext`
- DbContext lifetime mismatches, disposed query leaks, and cross-thread context capture
- `SaveChanges` or `SaveChangesAsync` inside loops
- Missing `AsNoTracking()` in read-only paths
- Unsafe raw SQL interpolation and constructed SQL strings
- Silent no-tracking writes and mixed tracking modes
- Bulk update/delete operations without an explicit filter

For Include and eager-loading review, see the [EF Core Include analyzer guide](/LinqContraband/ef-core-include-analyzer/).
For a focused walkthrough, see the [EF Core N+1 query detector guide](/LinqContraband/ef-core-n-plus-one-query-detector/).
For local methods, `AsEnumerable` boundaries, string comparison overloads, and `GroupBy` translation issues, see the
[EF Core client-side evaluation analyzer guide](/LinqContraband/ef-core-client-side-evaluation-analyzer/).
For DbContext lifetime, threading, and disposed-query review, see the
[EF Core DbContext lifetime analyzer guide](/LinqContraband/ef-core-dbcontext-lifetime-analyzer/).
For sync-over-async, `ToListAsync`, `SaveChangesAsync`, cancellation tokens, and async streams, see the
[EF Core async query analyzer guide](/LinqContraband/ef-core-async-query-analyzer/).
For missing tokens on `ToListAsync`, `FirstOrDefaultAsync`, and `SaveChangesAsync`, see the
[EF Core CancellationToken analyzer guide](/LinqContraband/ef-core-cancellation-token-analyzer/).
For deterministic pagination, see the [EF Core pagination OrderBy analyzer guide](/LinqContraband/ef-core-pagination-orderby-analyzer/).
For early `ToList`, `ToArray`, and `AsEnumerable` review, see the
[EF Core premature materialization analyzer guide](/LinqContraband/ef-core-premature-materialization-analyzer/).
For whole-entity loads, scalar projection, nested collection materializers, and identity `Select` calls, see the
[EF Core projection analyzer guide](/LinqContraband/ef-core-projection-analyzer/).
For security-sensitive SQL usage, see the [EF Core raw SQL injection analyzer guide](/LinqContraband/ef-core-raw-sql-injection-analyzer/).
For tracking-mode mistakes, see the [EF Core AsNoTracking analyzer guide](/LinqContraband/ef-core-asnotracking-analyzer/).
For set-based write review, see the [EF Core ExecuteUpdate analyzer guide](/LinqContraband/ef-core-executeupdate-analyzer/).
For repeated write review, see the [EF Core SaveChanges in loop analyzer guide](/LinqContraband/ef-core-savechanges-in-loop-analyzer/).
For pull-request enforcement, see the [EF Core query analyzer CI guide](/LinqContraband/ef-core-query-analyzer-ci/).

## Why Compile-Time Analysis Helps

EF Core problems often hide behind ordinary-looking LINQ. A query can compile, pass local testing, and still create a
large SQL result, many roundtrips, or a surprising tracking side effect. LinqContraband brings those issues into the IDE
and CI pipeline, where they are cheaper to fix.

The analyzer has no runtime cost. It ships as a NuGet analyzer package and works with common .NET development
environments including Visual Studio, Rider, VS Code, and CI builds.

## Browse the Rules

The full catalog contains 46 rules grouped by domain:

- [Rule catalog](/LinqContraband/rule-catalog.html)
- [EF Core analyzer rules guide](/LinqContraband/ef-core-analyzer-rules/)
- [EF Core query performance checklist](/LinqContraband/ef-core-query-performance-checklist/)
- [EF Core client-side evaluation analyzer](/LinqContraband/ef-core-client-side-evaluation-analyzer/)
- [EF Core async query analyzer](/LinqContraband/ef-core-async-query-analyzer/)
- [EF Core CancellationToken analyzer](/LinqContraband/ef-core-cancellation-token-analyzer/)
- [EF Core DbContext lifetime analyzer](/LinqContraband/ef-core-dbcontext-lifetime-analyzer/)
- [EF Core pagination OrderBy analyzer](/LinqContraband/ef-core-pagination-orderby-analyzer/)
- [EF Core premature materialization analyzer](/LinqContraband/ef-core-premature-materialization-analyzer/)
- [EF Core projection analyzer](/LinqContraband/ef-core-projection-analyzer/)
- [EF Core Include analyzer](/LinqContraband/ef-core-include-analyzer/)
- [EF Core N+1 query detector](/LinqContraband/ef-core-n-plus-one-query-detector/)
- [EF Core raw SQL injection analyzer](/LinqContraband/ef-core-raw-sql-injection-analyzer/)
- [EF Core AsNoTracking analyzer](/LinqContraband/ef-core-asnotracking-analyzer/)
- [EF Core ExecuteUpdate analyzer](/LinqContraband/ef-core-executeupdate-analyzer/)
- [EF Core SaveChanges in loop analyzer](/LinqContraband/ef-core-savechanges-in-loop-analyzer/)
- [EF Core query analyzer for CI](/LinqContraband/ef-core-query-analyzer-ci/)
- [LC007: N+1 query loops](/LinqContraband/LC007_NPlusOneLooper.html)
- [LC045: missing include](/LinqContraband/LC045_MissingInclude.html)
- [LC046: concurrent DbContext operations](/LinqContraband/LC046_ConcurrentDbContextOperations.html)
- [LC002: premature materialization](/LinqContraband/LC002_PrematureMaterialization.html)
- [LC018: interpolated raw SQL](/LinqContraband/LC018_AvoidFromSqlRawWithInterpolation.html)
- [LC037: constructed raw SQL strings](/LinqContraband/LC037_RawSqlStringConstruction.html)

## Official Links

- Canonical repository: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Official package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Maintainer: [George Wall](https://www.georgewall.uk/)
