# Spec: LC018 - Avoid FromSqlRaw with Interpolated Strings

## Goal
Detect usage of `FromSqlRaw` where the SQL string is an interpolated string or contains non-constant concatenations. This pattern is a major security risk as it can lead to SQL injection.

## The Problem
`FromSqlRaw` expects a raw SQL string and a separate array of parameters. If a developer uses string interpolation (`$"{var}"`), the variable is embedded directly into the SQL string before it reaches EF Core, bypassing parameterization.

### Example Violation
```csharp
// Violation: Potential SQL Injection
var name = "admin'; DROP TABLE Users; --";
var users = db.Users.FromSqlRaw($"SELECT * FROM Users WHERE Name = '{name}'").ToList();
```

### The Fix
Use `FromSqlInterpolated` (or `FromSql` in newer EF Core versions) which automatically handles parameterization for interpolated strings.

```csharp
// Correct: Safely parameterized
var name = "admin";
var users = db.Users.FromSqlInterpolated($"SELECT * FROM Users WHERE Name = {name}").ToList();
```

## Analyzer Logic

### ID: `LC018`
### Category: `Security`
### Severity: `Warning`

### Algorithm
1.  **Target Method**: Intercept invocations of `FromSqlRaw`.
2.  **Type Check**: Ensure the method is the EF Core extension method for `IQueryable` or `DbSet`.
3.  **Argument Analysis**: Inspect the first argument (the SQL string).
    -   If it's an **interpolated string** (`$""`): **VIOLATION**.
    -   If it's a **string concatenation** (`+`):
        -   Check if all parts are constant strings.
        -   If any part is a variable or non-constant: **VIOLATION**.

### Exceptions
-   If the interpolated string contains *only* constant values (though this is rare and usually better handled as a simple string).

## Test Cases

### Violations
```csharp
db.Users.FromSqlRaw($"SELECT * FROM Users WHERE Id = {id}");
db.Users.FromSqlRaw("SELECT * FROM Users WHERE Name = " + name);
```

### Valid
```csharp
db.Users.FromSqlRaw("SELECT * FROM Users");
db.Users.FromSqlRaw("SELECT * FROM Users WHERE Id = {0}", id);
db.Users.FromSqlInterpolated($"SELECT * FROM Users WHERE Id = {id}");
```

## Implementation Plan
1.  Create `LC018_AvoidFromSqlRawWithInterpolation` directory.
2.  Implement `AvoidFromSqlRawWithInterpolationAnalyzer`.
3.  Implement `AvoidFromSqlRawWithInterpolationFixer`.
4.  Implement tests.
