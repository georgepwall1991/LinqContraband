# Spec: LC020 - Avoid String.Contains with StringComparison in LINQ to Entities

## Goal
Detect usage of `string.Contains(string, StringComparison)` (and similar overloads for `StartsWith`, `EndsWith`) within LINQ queries targeted at Entity Framework Core. These overloads often cannot be translated to SQL, leading to client-side evaluation.

## The Problem
EF Core's SQL translation logic is optimized for the simple `string.Contains(string)` overload, which it maps to SQL `LIKE` or `CHARINDEX`. When a `StringComparison` argument is provided, EF Core frequently fails to find a direct SQL equivalent that matches the exact .NET comparison semantics, resulting either in a translation error or (in older versions) silent client-side evaluation of the entire dataset.

### Example Violation
```csharp
// Violation: Likely to trigger client-side evaluation or translation error
var users = db.Users.Where(u => u.Name.Contains("admin", StringComparison.OrdinalIgnoreCase)).ToList();
```

### The Fix
Use the simple overload. Most databases are case-insensitive by default, or you can configure the collation in EF Core. If you need specific comparison logic, it's better to handle it via database collation or explicit SQL functions.

```csharp
// Correct: Translates to SQL LIKE
var users = db.Users.Where(u => u.Name.Contains("admin")).ToList();
```

## Analyzer Logic

### ID: `LC020`
### Category: `Performance`
### Severity: `Warning`

### Algorithm
1.  **Target Methods**: Intercept invocations of:
    -   `string.Contains`
    -   `string.StartsWith`
    -   `string.EndsWith`
2.  **Overload Check**: Check if the invocation has an argument of type `System.StringComparison`.
3.  **Context Check**: Ensure the call is inside an `IQueryable` expression tree (e.g., inside a `Where`, `FirstOrDefault`, etc., that is eventually called on an `IQueryable`).
    -   *Simplification*: Check if the method is called on a property of an entity or within a LINQ extension method that takes a lambda.

### Exceptions
-   Calls on in-memory strings or `IEnumerable`.

## Test Cases

### Violations
```csharp
db.Users.Where(x => x.Name.Contains("abc", StringComparison.OrdinalIgnoreCase));
db.Users.Any(x => x.Email.StartsWith("test", StringComparison.CurrentCulture));
```

### Valid
```csharp
db.Users.Where(x => x.Name.Contains("abc"));
"some string".Contains("abc", StringComparison.OrdinalIgnoreCase); // Not in IQueryable context
```

## Implementation Plan
1.  Create `LC020_StringContainsWithComparison` directory.
2.  Implement `StringContainsWithComparisonAnalyzer`.
3.  Implement tests.
