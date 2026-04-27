# Spec: LC001 - Local Method Smuggler

## Goal
Detect usage of local C# methods in translation-critical LINQ to Entities query positions when the method depends on the query row.

## The Problem
Entity Framework Core attempts to translate LINQ expressions into SQL. When it encounters a method it does not recognize (like a custom local method), it often has to resort to "Client-Side Evaluation." This means it fetches ALL the data from the table into your application's memory and then filters it using C#. For large tables, this is a massive performance and memory leak.

Methods explicitly marked as translatable are exempt. LC001 does not report methods annotated with
`[Microsoft.EntityFrameworkCore.DbFunction]` or `[EntityFrameworkCore.Projectables.Projectable]`.

### Example Violation
```csharp
// CalculateAge is a local method. EF Core cannot translate it to SQL.
var users = db.Users.Where(u => CalculateAge(u.DateOfBirth) > 18).ToList();
```

### The Fix
Perform calculations outside the query or use translatable SQL functions.

```csharp
// Correct: Calculate the threshold date once
var minDob = DateTime.Now.AddYears(-18);
var users = db.Users.Where(u => u.DateOfBirth <= minDob).ToList();
```

## Analyzer Logic

### ID: `LC001`
### Category: `Performance`
### Severity: `Warning`

### Algorithm
1.  **Target**: Invocations within a lambda expression.
2.  **Context**: Ensure the lambda is an argument to an `IQueryable` method.
3.  **Dependency**: Require the method call to depend on the query lambda parameter.
4.  **Query position**: Report only translation-critical positions such as filters, ordering, joins, grouping keys, and predicates.
5.  **Check**: Skip known framework methods, trusted provider methods, and methods explicitly marked with
    `[DbFunction]` or `[Projectable]`.
6.  **Report**: If none of those exemptions apply, flag the invocation.

LC001 intentionally ignores helper calls that depend only on captured/input values and top-level `Select` projection helpers, where EF Core can often parameterize the captured value or perform the projection after the main query shape is translated.
