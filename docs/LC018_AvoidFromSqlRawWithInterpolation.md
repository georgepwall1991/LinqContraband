---
layout: default
title: "Spec: LC018 - Avoid FromSqlRaw with Interpolated Strings"
---

# Spec: LC018 - Avoid FromSqlRaw with Interpolated Strings

## Goal
Detect `FromSqlRaw(...)` and `SqlQueryRaw<T>(...)` calls where the SQL string is an interpolated string or contains non-constant concatenations. This pattern is a major security risk because it can lead to SQL injection.

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
LC018 covers two raw-SQL query entry points: `FromSqlRaw(...)` on an `IQueryable`/`DbSet`, and `SqlQueryRaw<T>(...)` on the `DbContext.Database` facade (`db.Database.SqlQueryRaw<int>($"...")`, the EF7+ scalar/keyless query form). Both take a raw `string sql` and are equal injection sinks. Their safe siblings — `FromSqlInterpolated` and `SqlQuery<T>` (which take a `FormattableString` and parameterize the holes) — are not flagged. The diagnostic message names the matching safe sibling for the call it found.

LC018 reports direct interpolated strings with non-constant interpolation holes and direct non-constant string concatenations passed to the `sql` argument of `FromSqlRaw(...)` or `SqlQueryRaw(...)`, including named `sql:` arguments. The constant-only safe-shape gate fires only when **every** hole is a compile-time constant: `const` fields, `const` locals, numeric/string literals, `nameof(...)`, and arithmetic over `const` operands all stay quiet. A `static readonly` field is *not* a compile-time constant in Roslyn's `IOperation.ConstantValue` sense and is treated as potentially unsafe — its value is observable and can be reassigned via reflection or non-trivial static initializers. A single non-constant hole in an otherwise constant interpolation still triggers, because a safe neighbour does not launder runtime data. The fixer is intentionally narrow: it is offered only for direct interpolated-string calls with no additional raw SQL parameters, where changing the method name preserves the argument flow. It rewrites `FromSqlRaw(...)` to `FromSqlInterpolated(...)` and `SqlQueryRaw<T>(...)` to `SqlQuery<T>(...)`. It is not offered when an interpolation hole appears inside SQL single quotes, such as `'{name}'`; remove the SQL quotes manually before using the safe interpolated API so EF can parameterize the value correctly.

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
db.Database.SqlQuery<int>($"SELECT Id FROM Users WHERE Id = {id}");
```

## Rule Boundary
- LC018 owns direct interpolated-string holes containing runtime data and direct non-constant `+` concatenation passed straight into `FromSqlRaw(...)`. It inspects the `sql` argument expression at the call site only — it does not follow the expression back through a local variable or parameter.
- LC018 requires the matched method to come from the EF Core namespace boundary (`Microsoft.EntityFrameworkCore` or a child namespace), not a same-named lookalike namespace.
- LC018 requires a queryable/DbSet receiver for `FromSqlRaw(...)` or a `DatabaseFacade` receiver for `SqlQueryRaw<T>(...)`, so same-named helpers in the EF namespace on unrelated receiver types stay quiet.
- LC018 fires regardless of how the receiver is reached: the instance call `dbSet.FromSqlRaw(...)`, the `DbContext.Set<T>().FromSqlRaw(...)` shape, and the static-extension form `RelationalQueryableExtensions.FromSqlRaw(query, ...)` all participate; the safe sibling `FromSqlInterpolated` stays quiet on every variant.
- LC037 owns the *upstream* construction flows that LC018 deliberately ignores: a local `var sql = $"..."` or `var sql = "..." + x` that later flows into `FromSqlRaw(sql)`, plus `string.Format(...)`, `string.Concat(...)`, `StringBuilder.Append(...)`, and `+=` accumulation. Aliased construction never double-fires LC018; LC037 picks it up, so both rules can stay narrow.

## Fixer Behavior
- Direct `FromSqlRaw($"... {value} ...")` calls with no extra raw SQL parameters become `FromSqlInterpolated($"... {value} ...")`.
- Direct `SqlQueryRaw<T>($"... {value} ...")` calls with no extra raw SQL parameters become `SqlQuery<T>($"... {value} ...")`; the generic type argument is preserved.
- Direct `+` concatenation still reports but is not auto-fixed, because the safe rewrite usually needs a new interpolated string or raw parameter list.
- Calls with additional raw SQL parameters are not auto-fixed; keep the SQL text constant and pass values through the raw API's parameter list.
- Interpolations inside SQL string literals, such as `'{name}'`, are not auto-fixed. Remove the SQL quotes first, then use `FromSqlInterpolated(...)` or `SqlQuery<T>(...)`.
