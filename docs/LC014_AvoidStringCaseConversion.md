---
layout: default
title: "Spec: LC014 - Avoid String Case Conversion in Queries"
---

# Spec: LC014 - Avoid String Case Conversion in Queries

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
