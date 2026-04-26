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

### Notes
LC018 reports direct interpolated strings and direct non-constant string concatenations passed to the `sql` argument of `FromSqlRaw(...)`, including named `sql:` arguments. The fixer is intentionally narrow: it is offered only for direct interpolated-string calls with no additional raw SQL parameters, where changing the method name to `FromSqlInterpolated` preserves the argument flow. It is not offered when an interpolation hole appears inside SQL single quotes, such as `'{name}'`; remove the SQL quotes manually before using `FromSqlInterpolated(...)` so EF can parameterize the value correctly.

## Test Cases

### Violations
```csharp
db.Users.FromSqlRaw($"SELECT * FROM Users WHERE Id = {id}");
db.Users.FromSqlRaw("SELECT * FROM Users WHERE Name = " + name);
```

### Valid
```csharp
var sql = $"SELECT * FROM Users WHERE Id = {id}";
db.Users.FromSqlRaw(sql); // LC037 owns constructed aliases that flow into raw SQL

db.Users.FromSqlRaw("SELECT * FROM Users");
db.Users.FromSqlRaw("SELECT * FROM Users WHERE Id = {0}", id);
db.Users.FromSqlInterpolated($"SELECT * FROM Users WHERE Id = {id}");
```

## Rule Boundary
- LC018 owns direct interpolated-string and direct non-constant `+` concatenation passed straight into `FromSqlRaw(...)`.
- LC037 covers broader constructed-SQL flows such as local aliases, `string.Format(...)`, `string.Concat(...)`, and `StringBuilder`.
