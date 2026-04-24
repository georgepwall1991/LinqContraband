# Spec: LC034 - Avoid ExecuteSqlRaw with Interpolation

## Goal
Detect unsafe SQL flowing into `ExecuteSqlRaw(...)` and `ExecuteSqlRawAsync(...)`.

## The Problem
`ExecuteSqlRaw` executes the SQL text exactly as provided. If interpolated or concatenated user input is baked into that text, you lose parameterization and invite SQL injection.

### Example Violation
```csharp
var name = GetUserInput();
await db.Database.ExecuteSqlRawAsync($"DELETE FROM Users WHERE Name = '{name}'");
```

### The Fix
Use the safe interpolated API so values are parameterized.

```csharp
await db.Database.ExecuteSqlAsync($"DELETE FROM Users WHERE Name = {name}");
```

## Analyzer Logic

### ID: `LC034`
### Category: `Security`
### Severity: `Warning`

### Notes
The fixer is intentionally narrow. It appears only for direct interpolated-string calls with no additional raw SQL
parameters, where the method-name rewrite keeps the SQL text and argument flow semantically safe.

## Rule Boundary
- LC034 owns direct interpolated-string and direct non-constant `+` concatenation passed straight into `ExecuteSqlRaw(...)` or `ExecuteSqlRawAsync(...)`.
- LC037 covers broader constructed-SQL flows such as local aliases, `string.Format(...)`, `string.Concat(...)`, and `StringBuilder`.
