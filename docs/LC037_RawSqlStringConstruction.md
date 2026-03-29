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
