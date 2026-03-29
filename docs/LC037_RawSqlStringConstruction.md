# Spec: LC037 - Raw SQL String Construction

## Goal
Detect string-built SQL before it reaches `FromSqlRaw(...)` or `ExecuteSqlRaw(...)`.

## The Problem
Concatenation, `string.Format(...)`, `string.Concat(...)`, and `StringBuilder` SQL assembly make it easy to smuggle unchecked values into raw SQL text.

### Example Violation
```csharp
var sql = "SELECT * FROM Users WHERE Name = '" + name + "'";
var users = db.Users.FromSqlRaw(sql).ToList();
```

### Safer Shape
Keep dynamic values out of the SQL string and pass them as parameters.

```csharp
var users = db.Users.FromSqlRaw("SELECT * FROM Users WHERE Name = {0}", name).ToList();
```

## Analyzer Logic

### ID: `LC037`
### Category: `Security`
### Severity: `Warning`

### Notes
This v1 surface is analyzer-only. It intentionally avoids speculative rewrites for complex SQL-building code.

## Rule Boundary
- LC037 intentionally yields when the raw SQL argument is passed directly as an interpolated string or direct non-constant `+` concatenation. Those direct call-site patterns are owned by LC018 (`FromSqlRaw`) and LC034 (`ExecuteSqlRaw` / `ExecuteSqlRawAsync`).
- LC037 still reports broader constructed-SQL shapes such as `string.Format(...)`, `string.Concat(...)`, `StringBuilder`, and local alias / variable flow into raw SQL APIs.
