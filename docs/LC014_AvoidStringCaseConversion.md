---
layout: default
title: "LC014: Avoid String Case Conversion in Queries"
description: "LC014 flags ToLower() and ToUpper() on entity properties in EF Core queries, which can stop the database using an index. Use a collation instead."
---

# LC014: Avoid String Case Conversion in Queries

## In Plain Terms

Imagine looking for "John" in a phone book. If you look for "John", you can
jump straight to 'J'. But if you decide to convert every single name in the book to lowercase first, you have to read
*every single name* from A to Z to check if it matches "john".

## Goal
Detect usage of `ToLower()` or `ToUpper()` on entity properties within LINQ queries.

## The Problem
Using `ToLower()` or `ToUpper()` on a database column in a `Where` clause prevents the database from using an index on that column. This forces a full table scan, which is very slow for large tables.

### Example Violation
```csharp
// Slow: Cannot use index on 'Email'
var user = db.Users.FirstOrDefault(u => u.Email.ToLower() == email.ToLower());
```

### The Fix
Use the database's default case-insensitive collation, an explicitly configured column collation, a normalized search column, or provider-specific collation support such as `EF.Functions.Collate` where it remains index-friendly.

```csharp
// Fast: Database can use index
var user = db.Users.FirstOrDefault(u => u.Email == email);
```

## Analyzer Logic

### ID: `LC014`
### Category: `Performance`
### Severity: `Warning`

### Notes
LC014 reports only when the query is rooted in an EF `DbSet<T>` or `DbContext.Set<T>()` chain, including simple local aliases assigned from those sources before the query operator. It covers both synchronous `Queryable` predicates (`Where`, `Any`, `Count`, `FirstOrDefault`, and similar terminals) and EF Core async predicate terminals such as `AnyAsync`, `CountAsync`, and `FirstOrDefaultAsync`. It stays quiet for explicit LINQ-to-Objects sources such as `new List<T>().AsQueryable()`, where database index usage is irrelevant.

The case-converted value counts as column-derived when it depends on the lambda parameter either through the **receiver** (`u.Name.ToLower()`, `u.Name.Substring(0, 3).ToUpper()`) or through a method's **string-carrying arguments** (`string.Concat(u.First, u.Last).ToLower()`, `string.Join("-", u.Tags).ToUpper()`, and even on a constant receiver such as `"prefix".Replace("x", u.Name).ToLower()`). A case conversion whose value derives only from constants or non-parameter locals (`string.Concat("a", "b").ToLower()`) stays quiet because it is computed client-side and never touches a column.

Only arguments that can carry a column's **text** into the result are followed. A column reaching a length/index/format argument does not make the result column-derived, because the lowercased text still comes from the (constant or constant-derived) receiver and the column only controls length, index, or format — so `"CONSTANT".PadRight(u.Name.Length).ToLower()`, `"HELLO".Substring(0, u.Age).ToLower()`, and `"HELLO".Remove(u.Age).ToLower()` stay quiet. In short, an argument reports when it flows the column's text into the result: a `string` argument, or a `char` argument that contributes a character (`string.Concat(u.Name[0]).ToUpper()`, `"x".Replace('x', u.Name[0]).ToLower()`). A value-type argument that only controls position or format (`int`/`bool`, or an enum such as `StringComparison`) does not.

For `Join` and `GroupJoin`, LC014 checks key selector lambdas against the source they belong to. Case conversion in an EF-backed outer or inner key selector can report, but a case conversion on an in-memory inner key selector or in the result selector stays quiet because it is not transforming a database column for filtering, joining, or ordering.

There is no safe automatic fix. Rewriting to `string.Equals(..., StringComparison.OrdinalIgnoreCase)` is provider- and version-sensitive in EF queries and can be untranslatable; it also overlaps with LC020's warning about `StringComparison` overloads in query expressions. Choose the database-specific fix deliberately based on collation and index design.

## .NET culture warnings inside EF Core queries

The .NET SDK's globalization rules (CA1862, CA1304, CA1305, CA1307, CA1309, CA1310, CA1311) also fire on these queries and suggest `string.Equals(..., StringComparison.OrdinalIgnoreCase)`, `ToLowerInvariant()`, or a `CultureInfo` / `IFormatProvider` argument. EF Core cannot translate any of those, so following the advice makes the query throw at run time. LinqContraband ships a suppressor that hides those CA warnings when the flagged call reads a parameter of a lambda EF Core translates (`Where`, `Select`, `FirstOrDefaultAsync` and the other query operators, query syntax, `HasQueryFilter`, `ExecuteUpdate` setters, and lambdas nested inside them). LC014 then carries the advice that fits a database query.

The CA warnings stay on in-memory LINQ (`list.Where(...)`, `list.AsQueryable()`), on calls that only touch captured variables (`name.ToLower()` runs in .NET before the query is sent), on expressions that are not passed to a query, and on value converters (`HasConversion`), which run in .NET. To keep a CA warning inside queries too, switch its suppression off in your project file. The suppression id is `LCS` plus the CA number (`LCS1304`, `LCS1305`, `LCS1307`, `LCS1309`, `LCS1310`, `LCS1311`, `LCS1862`):

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);LCS1862</NoWarn>
</PropertyGroup>
```

An `.editorconfig` severity for the suppression id has no effect; use `<NoWarn>`.
