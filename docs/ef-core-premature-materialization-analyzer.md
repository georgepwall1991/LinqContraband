---
layout: default
title: EF Core Premature Materialization Analyzer
description: Use LinqContraband as an EF Core premature materialization analyzer for ToList before Where, AsEnumerable client-side query work, unbounded ToList calls, and projection review.
permalink: /ef-core-premature-materialization-analyzer/
body_class: page-premature-materialization-analyzer
---

# EF Core Premature Materialization Analyzer

LinqContraband is an EF Core premature materialization analyzer for .NET projects that want `ToList`, `ToArray`,
`AsEnumerable`, and projection mistakes to show up during development and CI. It helps reviewers catch query work that
accidentally moves from SQL into memory before production row counts make the cost obvious.

Install the official analyzer package:

```bash
dotnet add package LinqContraband
```

## What Premature Materialization Looks Like

The risky shape is an EF Core query that crosses into LINQ-to-Objects before the filter, sort, projection, or aggregate
has finished:

```csharp
var users = db.Users
    .ToList()
    .Where(user => user.IsActive)
    .OrderBy(user => user.LastLogin);
```

`ToList()` and `ToArray()` execute the query immediately. `AsEnumerable()` is a little different: it does not fetch rows
by itself, but it changes the rest of the chain to `Enumerable`, so later operators are no longer provider-translated.

The safer shape keeps query work in SQL until the final materializer:

```csharp
var users = await db.Users
    .Where(user => user.IsActive)
    .OrderBy(user => user.LastLogin)
    .ToListAsync();
```

## LinqContraband Rules That Help

| Rule | What it detects | Review direction |
| --- | --- | --- |
| [LC002: premature materialization](/LinqContraband/LC002_PrematureMaterialization.html) | Inline `ToList`, `ToArray`, or `AsEnumerable` followed by provider-safe `Where`, `Select`, `OrderBy`, `Count`, and related operators. | Move the filter, sort, projection, or aggregate before the materializer. |
| [LC031: unbounded query materialization](/LinqContraband/LC031_UnboundedQueryMaterialization.html) | Broad `ToList` or similar materialization where the query has no proven row bound. | Add ordered pagination, `Take`, a single-row terminal, or a reviewed suppression for intentional full scans. |
| [LC017: whole entity projection](/LinqContraband/LC017_WholeEntityProjection.html) | Large entity loads where later code uses only a small subset of properties. | Project the fields or DTO shape the caller actually needs. |
| [LC041: single entity scalar projection](/LinqContraband/LC041_SingleEntityScalarProjection.html) | Single-entity materialization when the caller reads one scalar value. | Project the scalar in SQL before materialization. |
| [LC001: local method in IQueryable](/LinqContraband/LC001_LocalMethod.html) | Row-dependent local helpers inside translation-critical query lambdas. | Rewrite to translatable expressions or make the client boundary explicit and late. |

## Safer Query Patterns

Move filters before materialization:

```csharp
var activeUsers = await db.Users
    .Where(user => user.IsActive)
    .ToListAsync();
```

Project only the data the caller needs:

```csharp
var summaries = await db.Users
    .Where(user => user.IsActive)
    .Select(user => new UserSummary(user.Id, user.DisplayName))
    .ToListAsync();
```

Bound broad result sets:

```csharp
var page = await db.Users
    .OrderBy(user => user.Id)
    .Skip(pageIndex * pageSize)
    .Take(pageSize)
    .ToListAsync();
```

Keep client-side work explicit when it is intentional:

```csharp
var snapshot = await db.Users
    .Where(user => user.IsActive)
    .ToListAsync();

var sortedForDisplay = snapshot
    .OrderBy(user => NaturalSortKey(user.DisplayName))
    .ToList();
```

## Review Checklist

1. Does every `ToList`, `ToArray`, or `AsEnumerable` happen after the SQL-side filters, ordering, projection, and bounds?
2. Is an `AsEnumerable` boundary deliberate, or did it quietly move later operators into memory?
3. Could the query project a DTO or scalar instead of a full entity?
4. Is the result set bounded with ordered pagination, `Take`, or a single-row terminal?
5. If client-side work is required, is the boundary obvious and close to a comment or suppression that explains why?

## CI Severity Starter

Start with early materialization and broad reads as warnings, then promote them where the team has a clear exception
process:

```ini
[*.cs]

# Materialization and projection risk
dotnet_diagnostic.LC002.severity = warning
dotnet_diagnostic.LC031.severity = warning
dotnet_diagnostic.LC017.severity = suggestion
dotnet_diagnostic.LC041.severity = suggestion
```

Use the [EF Core query analyzer CI guide](/LinqContraband/ef-core-query-analyzer-ci/) when these diagnostics should run
on every pull request. Use the [EF Core query performance checklist](/LinqContraband/ef-core-query-performance-checklist/)
when reviewers need the broader query-performance context.

## Official Links

- Canonical repository: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Official NuGet package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Full rule catalog: [georgepwall1991.github.io/LinqContraband/rule-catalog.html](https://georgepwall1991.github.io/LinqContraband/rule-catalog.html)
- Safe install guidance: [Official LinqContraband downloads and authenticity](/LinqContraband/security-and-authenticity/)
