---
layout: default
title: EF Core Client-Side Evaluation Analyzer
description: Use LinqContraband as an EF Core client-side evaluation analyzer for local methods in IQueryable, early AsEnumerable or ToList boundaries, StringComparison overloads, column case conversion, and non-translatable GroupBy projections.
permalink: /ef-core-client-side-evaluation-analyzer/
body_class: page-client-side-evaluation-analyzer
---

# EF Core Client-Side Evaluation Analyzer

LinqContraband helps teams catch EF Core query shapes that cannot stay cleanly in SQL. It reports row-dependent local
methods inside `IQueryable`, early `AsEnumerable` or `ToList` boundaries, provider-sensitive string comparison
overloads, column case conversion, and risky `GroupBy` projections before they become production runtime failures or
large in-memory scans.

Install the official NuGet package:

```bash
dotnet add package LinqContraband
```

## Why Client-Side Evaluation Still Matters

Modern EF Core usually throws when a non-translatable query expression appears before the final projection. That is
better than silently loading a table, but it is still late feedback: the query compiles, reaches production-shaped data,
and fails or gets "fixed" by moving too much work into memory.

```csharp
var users = await db.Users
    .Where(user => IsActiveAdult(user)) // LC001: local method in IQueryable
    .ToListAsync(cancellationToken);
```

Prefer SQL-translatable expressions, captured constants, mapped functions, or an explicit late client boundary:

```csharp
var minimumBirthDate = today.AddYears(-18);

var users = await db.Users
    .Where(user => user.IsActive && user.DateOfBirth <= minimumBirthDate)
    .ToListAsync(cancellationToken);
```

## Rules Covered By This Guide

| Rule | What it catches | Review direction |
| --- | --- | --- |
| [LC001: local method in IQueryable](/LinqContraband/LC001_LocalMethod.html) | Row-dependent source helpers inside translation-critical query lambdas. | Rewrite as a translatable expression, mapped database function, computed column, or explicit late client boundary. |
| [LC002: premature materialization](/LinqContraband/LC002_PrematureMaterialization.html) | `ToList`, `ToArray`, or `AsEnumerable` before provider-safe filtering, sorting, projection, or aggregation. | Move SQL-safe query work before materialization. |
| [LC014: string case conversion](/LinqContraband/LC014_AvoidStringCaseConversion.html) | `ToLower` or `ToUpper` on column-derived values inside EF queries. | Use collation, normalized columns, or provider-specific functions deliberately. |
| [LC020: StringComparison overloads](/LinqContraband/LC020_StringContainsWithComparison.html) | `Contains`, `StartsWith`, or `EndsWith` overloads with `StringComparison` inside query expressions. | Use database collation, normalized search data, or a provider-specific API instead of .NET comparison semantics. |
| [LC024: non-translatable GroupBy projection](/LinqContraband/LC024_GroupByNonTranslatable.html) | Grouping projections that materialize or inspect group elements instead of reducing to server aggregates. | Keep `GroupBy` projections aggregate-only, or materialize intentionally before complex grouping. |

## Common Client-Evaluation Traps

### Local helper inside a query

```csharp
var users = db.Users
    .Where(user => CalculateAge(user.DateOfBirth) >= 18) // LC001
    .ToList();
```

```csharp
var minimumBirthDate = now.AddYears(-18);

var users = db.Users
    .Where(user => user.DateOfBirth <= minimumBirthDate)
    .ToList();
```

### AsEnumerable too early

```csharp
var users = db.Users
    .AsEnumerable()
    .Where(user => user.IsActive) // LC002
    .ToList();
```

```csharp
var users = db.Users
    .Where(user => user.IsActive)
    .ToList();
```

### StringComparison inside IQueryable

```csharp
var users = db.Users
    .Where(user => user.Email.Contains(term, StringComparison.OrdinalIgnoreCase)) // LC020
    .ToList();
```

```csharp
var users = db.Users
    .Where(user => user.NormalizedEmail.Contains(normalizedTerm))
    .ToList();
```

### Case conversion on a database column

```csharp
var user = db.Users
    .FirstOrDefault(user => user.Email.ToLower() == email.ToLower()); // LC014
```

Prefer an indexed normalized column or a reviewed database collation strategy:

```csharp
var user = db.Users
    .FirstOrDefault(user => user.NormalizedEmail == normalizedEmail);
```

### GroupBy projection that pulls group items

```csharp
var totals = db.Orders
    .GroupBy(order => order.CustomerId)
    .Select(group => new
    {
        group.Key,
        Orders = group.ToList() // LC024
    });
```

```csharp
var totals = db.Orders
    .GroupBy(order => order.CustomerId)
    .Select(group => new
    {
        group.Key,
        Count = group.Count(),
        Total = group.Sum(order => order.Total)
    });
```

## Safer Translation Patterns

- Keep `Where`, `OrderBy`, `Select`, `GroupBy`, and aggregates provider-side until the final materializer.
- Move row-independent calculations into captured locals before the query.
- Replace row-dependent helper methods with translatable expressions, `[DbFunction]` methods, computed columns, or
  provider-supported projectables.
- Use normalized columns or explicit database collation for case-insensitive search.
- Keep complex client-only work behind a visible `ToListAsync` or `AsEnumerable` boundary after filtering and bounds.
- Suppress narrowly when a small table or deliberate client boundary is an accepted trade-off.

## Review Checklist

1. Does any query lambda call a source-defined helper that depends on the row?
2. Does `AsEnumerable`, `ToList`, or `ToArray` appear before filters, ordering, projection, or aggregation that SQL could handle?
3. Are string search semantics handled by database collation or normalized data rather than `StringComparison` overloads?
4. Does any query lower or upper-case a column before comparison?
5. Do `GroupBy` projections reduce to aggregates instead of materializing each group?

## CI Severity Starter

```ini
[*.cs]

# Client-side evaluation and translation risk
dotnet_diagnostic.LC001.severity = warning
dotnet_diagnostic.LC002.severity = warning
dotnet_diagnostic.LC014.severity = warning
dotnet_diagnostic.LC020.severity = warning
dotnet_diagnostic.LC024.severity = warning
```

Use the [EF Core query analyzer CI guide](/LinqContraband/ef-core-query-analyzer-ci/) to promote selected translation
rules from local warnings to pull-request checks.

## Related Guides

- [EF Core premature materialization analyzer](/LinqContraband/ef-core-premature-materialization-analyzer/)
- [EF Core LINQ performance analyzer](/LinqContraband/ef-core-linq-performance-analyzer/)
- [EF Core query performance checklist](/LinqContraband/ef-core-query-performance-checklist/)
- [EF Core analyzer rules](/LinqContraband/ef-core-analyzer-rules/)
- [EF Core query analyzer for CI](/LinqContraband/ef-core-query-analyzer-ci/)

## Official Links

- Canonical repository: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Official NuGet package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Documentation hub: [georgepwall1991.github.io/LinqContraband](https://georgepwall1991.github.io/LinqContraband/)
