---
layout: default
title: "LC018: Avoid FromSqlRaw with Interpolated Strings"
description: "LC018 flags FromSqlRaw and SqlQueryRaw called with interpolated or concatenated SQL, an injection risk. Use FromSql or SQL parameters instead."
---

# LC018: Avoid FromSqlRaw with Interpolated Strings

## In Plain Terms

Imagine a bank where you write your name on a slip to get money. If you use
a special pen that lets you erase "Name: John" and write "Give John everything in the vault," you've just robbed the
bank. Raw query SQL with interpolated strings is like using that erasable pen.

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
Use `FromSql` (EF Core 7+), which turns every interpolation hole into a SQL parameter. On EF Core 6 and older the same API is called `FromSqlInterpolated`; EF Core 11 marks that name obsolete.

```csharp
// Correct: Safely parameterized
var name = "admin";
var users = db.Users.FromSql($"SELECT * FROM Users WHERE Name = {name}").ToList();
```

## Overlap with EF Core's own analyzers
EF Core ships analyzers with the `Microsoft.EntityFrameworkCore` package. Since EF Core 8, **EF1002** reports an interpolated string passed straight to `FromSqlRaw`/`SqlQueryRaw`, and since EF Core 10, **EF1003** reports a concatenated one. Both come with EF's own fix to `FromSql`/`SqlQuery`.

LC018 does not add a second warning to those lines. It stays quiet when the call is the relational `FromSqlRaw`/`SqlQueryRaw` from `Microsoft.EntityFrameworkCore.Relational` 8.0 or later (10.0 or later for concatenation) and the SQL is the call's second positional argument, which is exactly what EF checks. LC018 still reports:

- EF Core 7 and older, where EF has no raw-SQL analyzer, and concatenation on EF Core 8 and 9.
- A reordered named argument such as `FromSqlRaw(parameters: args, sql: $"... {id}")`, which EF's analyzer does not see.
- Raw SQL APIs outside `Microsoft.EntityFrameworkCore.Relational`, such as Cosmos `FromSqlRaw`.

The `security`, `critical` and `strict` [presets](https://github.com/georgepwall1991/LinqContraband#configuration) raise EF1002 and EF1003 to errors along with LC018. If you set severities by hand, raise EF1002 and EF1003 too (`dotnet_diagnostic.EF1002.severity = error`). If your build excludes EF Core's analyzers or disables EF1002/EF1003, turn the deferral off so LC018 reports every call:

```ini
[*.cs]
dotnet_code_quality.LC018.defer_to_ef_analyzers = false
```

## Analyzer Logic

### ID: `LC018`
### Category: `Security`
### Severity: `Warning`

### Notes
LC018 covers two raw-SQL query entry points: `FromSqlRaw(...)` on an `IQueryable`/`DbSet`, and `SqlQueryRaw<T>(...)` on the `DbContext.Database` facade (`db.Database.SqlQueryRaw<int>($"...")`, the EF7+ scalar/keyless query form). Both take a raw `string sql` and are equal injection sinks. Their safe siblings — `FromSql`/`FromSqlInterpolated` and `SqlQuery<T>` (which take a `FormattableString` and parameterize the holes) — are not flagged. The diagnostic message names the matching safe sibling for the call it found.

LC018 reports direct interpolated strings with non-constant interpolation holes and direct non-constant string concatenations passed to the `sql` argument of `FromSqlRaw(...)` or `SqlQueryRaw(...)`, including named `sql:` arguments. The constant-only safe-shape gate fires only when **every** hole is a compile-time constant: `const` fields, `const` locals, numeric/string literals, `nameof(...)`, and arithmetic over `const` operands all stay quiet. A `static readonly` field is *not* a compile-time constant in Roslyn's `IOperation.ConstantValue` sense and is treated as potentially unsafe — its value is observable and can be reassigned via reflection or non-trivial static initializers. A single non-constant hole in an otherwise constant interpolation still triggers, because a safe neighbour does not launder runtime data. The fixer is intentionally narrow: it is offered only for direct interpolated-string calls with no additional raw SQL parameters, where every interpolation hole is in a likely SQL value position such as after `=`, `<`, `>`, `LIKE`, or `IN (`. It rewrites `FromSqlRaw(...)` to `FromSql(...)` (or `FromSqlInterpolated(...)` on EF Core 6 and older) and `SqlQueryRaw<T>(...)` to `SqlQuery<T>(...)`. It is not offered when an interpolation hole appears inside SQL single quotes, such as `'{name}'`; remove the SQL quotes manually before using the safe interpolated API so EF can parameterize the value correctly. It is also not offered when a hole appears where SQL expects an identifier or structural fragment, such as `SELECT {columnName}`, `FROM {tableName}`, `WHERE {columnName} = 1`, `EXEC {procedureName}`, `JOIN {tableName}`, or `ORDER BY {columnName}`; those cases still report because raw SQL structure built from runtime data needs manual allow-listing or query redesign rather than value parameterization.

## Test Cases

### Violations
```csharp
// On EF Core 8+ the first line is EF1002 instead, and on EF Core 10+ the second is EF1003.
db.Users.FromSqlRaw($"SELECT * FROM Users WHERE Id = {id}");
db.Users.FromSqlRaw("SELECT * FROM Users WHERE Name = " + name);

// Reported on every EF Core version: EF1002 does not see a reordered named argument.
db.Users.FromSqlRaw(parameters: args, sql: $"SELECT * FROM Users WHERE Id = {id}");
```

### Valid
```csharp
var sql = $"SELECT * FROM Users WHERE Id = {id}";
db.Users.FromSqlRaw(sql); // LC037 owns constructed aliases that flow into raw SQL

db.Users.FromSqlRaw("SELECT * FROM Users");
db.Users.FromSqlRaw("SELECT * FROM Users WHERE Id = {0}", id);
db.Users.FromSql($"SELECT * FROM Users WHERE Id = {id}");
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
- Direct `FromSqlRaw($"... {value} ...")` calls with no extra raw SQL parameters become `FromSql($"... {value} ...")`. The fixer checks what the rewritten call binds to: it uses `FromSql` when that overload exists (EF Core 7+, and Cosmos on EF Core 9+), falls back to `FromSqlInterpolated` on older EF Core, and offers nothing when the only candidate is obsolete or missing, so the fix never introduces CS0618 or a compile error.
- Direct `SqlQueryRaw<T>($"... {value} ...")` calls with no extra raw SQL parameters become `SqlQuery<T>($"... {value} ...")`; the generic type argument is preserved.
- Direct `+` concatenation still reports but is not auto-fixed, because the safe rewrite usually needs a new interpolated string or raw parameter list.
- Calls with additional raw SQL parameters are not auto-fixed; keep the SQL text constant and pass values through the raw API's parameter list.
- Interpolations inside SQL string literals, such as `'{name}'`, are not auto-fixed. Remove the SQL quotes first, then use `FromSql(...)` or `SqlQuery<T>(...)`.
- Interpolations outside likely SQL value positions, such as `FROM {tableName}`, `SELECT {columnName}`, `WHERE {columnName} = 1`, or `EXEC {procedureName}`, are not auto-fixed. `FromSql(...)` would parameterize the hole as a value, not a table, column, schema, procedure, or ordering fragment.
