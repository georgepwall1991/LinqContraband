---
layout: default
title: EF Core Pagination OrderBy Analyzer
description: Use LinqContraband as an EF Core pagination analyzer for missing OrderBy before Skip, Take, Last, ElementAt, Chunk, and misplaced OrderBy calls.
permalink: /ef-core-pagination-orderby-analyzer/
body_class: page-pagination-orderby-analyzer
---

# EF Core Pagination OrderBy Analyzer

LinqContraband includes an EF Core pagination analyzer for query shapes where row order matters. It catches missing
`OrderBy` before `Skip`, `Take`, `Last`, `ElementAt`, and `Chunk`, plus `OrderBy` calls that appear after pagination has
already selected an unstable page.

Install the official analyzer package:

```bash
dotnet add package LinqContraband
```

## Why Pagination Needs a Stable Order

SQL result order is not guaranteed unless the query asks for one. A query can look stable on a developer machine, then
return duplicate or missing rows in production after an index change, statistics update, or different execution plan.

```csharp
var page = await db.Users
    .Where(user => user.IsActive)
    .Skip(pageIndex * pageSize)
    .Take(pageSize)
    .ToListAsync();
```

That is not deterministic pagination. The database is free to choose any row order before applying the page window.

Prefer an explicit order before the page boundary:

```csharp
var page = await db.Users
    .Where(user => user.IsActive)
    .OrderBy(user => user.Id)
    .Skip(pageIndex * pageSize)
    .Take(pageSize)
    .ToListAsync();
```

## What The Analyzer Flags

| Rule | Finds | Why it matters |
| --- | --- | --- |
| [LC015: missing OrderBy](/LinqContraband/LC015_MissingOrderBy.html) | `Skip`, `Take`, `Last`, `LastOrDefault`, `ElementAt`, `ElementAtOrDefault`, and `Chunk` on unordered EF Core queries. | Pagination, positional access, and "last row" lookups need a stable order. |
| [LC015: misplaced OrderBy](/LinqContraband/LC015_MissingOrderBy.html) | `OrderBy` after `Skip` or `Take`. | Sorting an already selected page does not make the page selection deterministic. |
| [LC005: multiple OrderBy](/LinqContraband/LC005_MultipleOrderBy.html) | A later `OrderBy` that resets an earlier sort. | Accidental sort reset can hide the intended page order. |
| [LC031: unbounded materialization](/LinqContraband/LC031_UnboundedQueryMaterialization.html) | Broad reads without a bound. | Pagination should be explicit when a query can return many rows. |

## Misplaced OrderBy Example

This sorts only the arbitrary 25 rows already chosen by `Take`:

```csharp
var products = await db.Products
    .Take(25)
    .OrderBy(product => product.Name)
    .ToListAsync();
```

Move the ordering before `Take`:

```csharp
var products = await db.Products
    .OrderBy(product => product.Name)
    .Take(25)
    .ToListAsync();
```

When the sort column can tie, add a stable tiebreaker:

```csharp
var products = await db.Products
    .OrderBy(product => product.Name)
    .ThenBy(product => product.Id)
    .Skip(pageIndex * pageSize)
    .Take(pageSize)
    .ToListAsync();
```

## Safer Pagination Patterns

- Order by a primary key when any stable order is acceptable.
- Order by the business sort column and then by a unique tiebreaker for visible lists.
- Use every part of a composite key when the key is the ordering contract.
- Keep `OrderBy` before `Skip`, `Take`, `Last`, `ElementAt`, or `Chunk`.
- Do not rely on insertion order, clustered-index luck, or "natural" database order.

## Review Checklist

1. Does every `Skip` or `Take` have an upstream `OrderBy`?
2. Does visible sorting include a deterministic tiebreaker when values can tie?
3. Is `OrderBy` placed before the page boundary rather than after it?
4. Could a second `OrderBy` reset an earlier `OrderBy` that the query depends on?
5. Does the query also need a `Take` or page size bound before materialization?

## CI Severity Starter

Unordered pagination is usually a production reliability issue, so LC015 is a good early warning:

```ini
[*.cs]

# Deterministic pagination and row order
dotnet_diagnostic.LC015.severity = warning
dotnet_diagnostic.LC005.severity = warning
dotnet_diagnostic.LC031.severity = warning
```

Use the [EF Core query analyzer CI guide](/LinqContraband/ef-core-query-analyzer-ci/) when these diagnostics should run
on every pull request. Use the [EF Core query performance checklist](/LinqContraband/ef-core-query-performance-checklist/)
when reviewers need the broader query-performance context.

## Official Links

- Canonical repository: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Official NuGet package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Full rule catalog: [georgepwall1991.github.io/LinqContraband/rule-catalog.html](https://georgepwall1991.github.io/LinqContraband/rule-catalog.html)
- Safe install guidance: [Official LinqContraband downloads and authenticity](/LinqContraband/security-and-authenticity/)
