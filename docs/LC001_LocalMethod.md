# Spec: LC001 - Local Method Smuggler

## Goal
Detect usage of local C# methods within LINQ to Entities queries that cannot be translated to SQL by the database provider.

## The Problem
Entity Framework Core attempts to translate LINQ expressions into SQL. When it encounters a method it does not recognize (like a custom local method), it often has to resort to "Client-Side Evaluation." This means it fetches ALL the data from the table into your application's memory and then filters it using C#. For large tables, this is a massive performance and memory leak.

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
3.  **Check**: If the method invoked is not a known framework method or a trusted database function (e.g., `EF.Functions`), flag it.
