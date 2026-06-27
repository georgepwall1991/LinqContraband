---
layout: default
title: EF Core Projection Analyzer
description: Use LinqContraband as an EF Core projection analyzer for whole-entity loads, scalar projection opportunities, nested ToList in Select, and redundant identity Select calls.
permalink: /ef-core-projection-analyzer/
body_class: page-projection-analyzer
---

# EF Core Projection Analyzer

LinqContraband is an EF Core projection analyzer for .NET teams that want over-fetching, noisy `Select` calls, and
provider-sensitive projection shapes to show up during development and CI. It helps reviewers catch whole-entity loads
when code only needs a few columns, single-entity queries that only consume one scalar, nested `ToList` calls inside
projections, and redundant `Select(x => x)` chains.

Install the official analyzer package:

```bash
dotnet add package LinqContraband
```

## Why Projection Shape Matters

EF Core can translate a precise `Select` into SQL that reads only the columns a caller needs. Without projection, a
query can pull full rows, allocate unused entity properties, and track data that is only used for display or a simple
scalar result.

The expensive shape loads full entities and then reads one property:

```csharp
var users = await db.Users
    .Where(user => user.IsActive)
    .ToListAsync(cancellationToken);

foreach (var user in users)
{
    names.Add(user.DisplayName); // LC017
}
```

The safer shape projects the required data before materialization:

```csharp
var names = await db.Users
    .Where(user => user.IsActive)
    .Select(user => user.DisplayName)
    .ToListAsync(cancellationToken);
```

If the main issue is an early `ToList` or `AsEnumerable` boundary, use the
[EF Core premature materialization analyzer guide](/LinqContraband/ef-core-premature-materialization-analyzer/).

## LinqContraband Rules That Help

| Rule | What it detects | Review direction |
| --- | --- | --- |
| [LC017: whole entity projection](/LinqContraband/LC017_WholeEntityProjection.html) | Large entity loads where later code uses only a small subset of properties. | Project a DTO, anonymous type, or scalar sequence with the fields the caller actually needs. |
| [LC041: single entity scalar projection](/LinqContraband/LC041_SingleEntityScalarProjection.html) | `First`, `Single`, and related single-row queries where the local code consumes one scalar property. | Project the scalar before materialization when the rewrite preserves no-row behaviour. |
| [LC022: nested collection materialization inside projection](/LinqContraband/LC022_ToListInSelectProjection.html) | `ToList`, `ToArray`, or similar materializers inside projected collection members. | Review whether the shape should stay provider-friendly, use split-query shaping, or intentionally return a concrete collection. |
| [LC029: redundant identity Select](/LinqContraband/LC029_RedundantIdentitySelect.html) | `Select(x => x)` or equivalent identity projections on queryable and enumerable chains. | Remove the redundant projection while preserving any intentional boundary such as `AsEnumerable`. |

## Common EF Core Projection Problems

### Whole entity loaded for one or two fields

```csharp
var products = await db.Products
    .Where(product => product.Price > 100)
    .ToListAsync(cancellationToken); // LC017

return products.Select(product => product.Name).ToList();
```

```csharp
return await db.Products
    .Where(product => product.Price > 100)
    .Select(product => product.Name)
    .ToListAsync(cancellationToken);
```

### Single entity loaded for one scalar

```csharp
var user = await db.Users
    .FirstAsync(user => user.IsActive, cancellationToken); // LC041

return user.Email;
```

```csharp
return await db.Users
    .Where(user => user.IsActive)
    .Select(user => user.Email)
    .FirstAsync(cancellationToken);
```

### Nested materializer inside Select

```csharp
var customers = db.Customers
    .Select(customer => new
    {
        customer.Id,
        OrderIds = customer.Orders.Select(order => order.Id).ToList() // LC022
    });
```

```csharp
var customers = db.Customers
    .Select(customer => new
    {
        customer.Id,
        OrderIds = customer.Orders.Select(order => order.Id)
    });
```

### Identity projection noise

```csharp
var users = await db.Users
    .Where(user => user.IsActive)
    .Select(user => user) // LC029
    .ToListAsync(cancellationToken);
```

```csharp
var users = await db.Users
    .Where(user => user.IsActive)
    .ToListAsync(cancellationToken);
```

## Projection Review Checklist

1. Does the caller need tracked entities, or would a DTO, anonymous type, scalar, or record be enough?
2. Are large entities loaded when only one or two properties are used locally?
3. Can a single-row query project the consumed scalar before `First`, `Single`, or their async equivalents?
4. Is a nested collection materializer inside `Select` required by the DTO contract, or can the provider shape stay lazy?
5. Is `Select(x => x)` just noise, or is the real intent a visible client-side boundary such as `AsEnumerable`?

## CI Severity Starter

Start projection rules as suggestions while existing query shapes are cleaned up, then promote LC017 or LC041 where the
team has a clear DTO/projection policy:

```ini
[*.cs]

# Projection and over-fetching
dotnet_diagnostic.LC017.severity = suggestion
dotnet_diagnostic.LC041.severity = suggestion
dotnet_diagnostic.LC022.severity = suggestion
dotnet_diagnostic.LC029.severity = suggestion
```

Use the [EF Core query analyzer CI guide](/LinqContraband/ef-core-query-analyzer-ci/) when projection diagnostics
should appear on every pull request. Use the
[EF Core query performance checklist](/LinqContraband/ef-core-query-performance-checklist/) when reviewers need the
broader query-performance context.

## Related Guides

- [EF Core premature materialization analyzer](/LinqContraband/ef-core-premature-materialization-analyzer/)
- [EF Core query performance checklist](/LinqContraband/ef-core-query-performance-checklist/)
- [EF Core analyzer rules](/LinqContraband/ef-core-analyzer-rules/)
- [EF Core async query analyzer](/LinqContraband/ef-core-async-query-analyzer/)
- [EF Core AsNoTracking analyzer](/LinqContraband/ef-core-asnotracking-analyzer/)

## Official Links

- Canonical repository: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Official NuGet package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Full rule catalog: [georgepwall1991.github.io/LinqContraband/rule-catalog.html](https://georgepwall1991.github.io/LinqContraband/rule-catalog.html)
- Safe install guidance: [Official LinqContraband downloads and authenticity](/LinqContraband/security-and-authenticity/)
