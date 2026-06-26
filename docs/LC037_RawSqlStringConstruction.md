---
layout: default
title: "Spec: LC037 - Raw SQL String Construction"
---

# Spec: LC037 - Raw SQL String Construction

## Goal
Detect string-built SQL before it reaches `FromSqlRaw(...)`, `ExecuteSqlRaw(...)`, or `SqlQueryRaw<T>(...)` (the EF7+ scalar/keyless raw-SQL query on `DbContext.Database`).

## The Problem
Concatenation, `string.Format(...)`, `string.Concat(...)`, and `StringBuilder` SQL assembly make it easy to smuggle unchecked values into raw SQL text.

### Example Violation
```csharp
var sql = "SELECT * FROM Users WHERE Name = '" + name + "'";
var users = db.Users.FromSqlRaw(sql).ToList();
```

`LC037` also catches construction that is hidden behind common helper APIs:

```csharp
var sql = string.Format("SELECT * FROM Users WHERE Name = '{0}'", name);
var users = db.Users.FromSqlRaw(sql).ToList();
```

```csharp
var sql = string.Concat("DELETE FROM Users WHERE Name = '", name, "'");
db.Database.ExecuteSqlRaw(sql);
```

```csharp
var sql = new StringBuilder()
    .Append("SELECT * FROM Users WHERE Name = '")
    .Append(name)
    .Append("'")
    .ToString();

var names = db.Database.SqlQueryRaw<string>(sql);
```

### Safer Shape
Keep dynamic values out of the SQL string and pass them as parameters.

```csharp
var users = db.Users.FromSqlRaw("SELECT * FROM Users WHERE Name = {0}", name).ToList();
```

The same pattern applies to raw execution and scalar/keyless raw SQL:

```csharp
db.Database.ExecuteSqlRaw("DELETE FROM Users WHERE Name = {0}", name);
var names = db.Database.SqlQueryRaw<string>("SELECT Name FROM Users WHERE Name = {0}", name);
```

## Analyzer Logic

### ID: `LC037`
### Category: `Security`
### Severity: `Warning`

### Notes
This v1 surface is analyzer-only. It intentionally avoids speculative rewrites for complex SQL-building code.

The rule reports where the constructed string reaches the raw SQL API. The fix depends on the SQL shape: remove embedded quotes around values, keep the command/query text constant, and pass values through EF Core parameters instead of assembling them into the SQL string.

## Rule Boundary
- LC018 owns direct interpolated strings and direct non-constant `+` concatenation passed to query APIs (`FromSqlRaw(...)` and `SqlQueryRaw<T>(...)`).
- LC034 owns direct interpolated strings and direct non-constant `+` concatenation passed to raw execution APIs (`ExecuteSqlRaw(...)` and `ExecuteSqlRawAsync(...)`).
- LC037 intentionally yields for those direct call-site patterns so a single raw SQL expression is not double-reported.
- LC037 still reports broader constructed-SQL shapes such as `string.Format(...)`, `string.Concat(...)`, `StringBuilder`, and local alias / variable flow into all three raw SQL sink families: `FromSqlRaw(...)`, `ExecuteSqlRaw(...)` / `ExecuteSqlRawAsync(...)`, and `SqlQueryRaw<T>(...)`.
- `SqlQueryRaw<T>` is split deliberately: direct interpolation and direct `+` concatenation are LC018's territory, while `string.Format(...)`, `string.Concat(...)`, `StringBuilder`, and aliased local construction are LC037's.
- For simple local variables, LC037 resolves the latest guaranteed declaration or assignment before the raw SQL call, so an earlier constructed value overwritten unconditionally by a constant is ignored while later constructed overwrites are still reported.
- Conditional overwrites are treated conservatively: a branch-only constant assignment does not suppress an earlier constructed SQL value, and a branch-only constructed assignment remains suspicious unless a later guaranteed assignment overwrites it.
