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
LC014 reports only when the query is rooted in an EF `DbSet<T>` or `DbContext.Set<T>()` chain, including simple local aliases assigned from those sources before the query operator. It stays quiet for explicit LINQ-to-Objects sources such as `new List<T>().AsQueryable()`, where database index usage is irrelevant.

The case-converted value counts as column-derived when it depends on the lambda parameter either through the **receiver** (`u.Name.ToLower()`, `u.Name.Substring(0, 3).ToUpper()`) or through a method's **arguments** (`string.Concat(u.First, u.Last).ToLower()`, `string.Join("-", u.Tags).ToUpper()`). A case conversion whose value derives only from constants or non-parameter locals (`string.Concat("a", "b").ToLower()`) stays quiet because it is computed client-side and never touches a column.

For `Join` and `GroupJoin`, LC014 checks key selector lambdas against the source they belong to. Case conversion in an EF-backed outer or inner key selector can report, but a case conversion on an in-memory inner key selector or in the result selector stays quiet because it is not transforming a database column for filtering, joining, or ordering.

There is no safe automatic fix. Rewriting to `string.Equals(..., StringComparison.OrdinalIgnoreCase)` is provider- and version-sensitive in EF queries and can be untranslatable; it also overlaps with LC020's warning about `StringComparison` overloads in query expressions. Choose the database-specific fix deliberately based on collation and index design.
