---
layout: default
title: "LC034: Avoid ExecuteSqlRaw with Interpolation"
description: "LC034 flags ExecuteSqlRaw and ExecuteSqlRawAsync called with interpolated or concatenated SQL, an injection risk. Use ExecuteSql or parameters."
---

# LC034: Avoid ExecuteSqlRaw with Interpolation

## In Plain Terms

Imagine writing a note to the cafeteria that says "Throw away lunch for
${name}". If somebody scribbles extra instructions into the blank, the cafeteria might throw away *everybody's* lunch.
You want a form with a locked box for the name instead.

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
Use the safe interpolated API so values are parameterized by EF Core.

```csharp
await db.Database.ExecuteSqlAsync($"DELETE FROM Users WHERE Name = {name}");
```

If the SQL is not naturally an interpolated string at the call site, keep the SQL text constant and pass values through the raw API's parameter list.

```csharp
await db.Database.ExecuteSqlRawAsync(
    "DELETE FROM Users WHERE Name = {0}",
    name);
```

## Overlap with EF Core's own analyzers
EF Core ships analyzers with the `Microsoft.EntityFrameworkCore` package. Since EF Core 8, **EF1002** reports an interpolated string passed straight to `ExecuteSqlRaw`/`ExecuteSqlRawAsync`, and since EF Core 10, **EF1003** reports a concatenated one. Both come with EF's own fix to `ExecuteSql`/`ExecuteSqlAsync`.

LC034 does not add a second warning to those lines. It stays quiet when the call comes from `Microsoft.EntityFrameworkCore.Relational` 8.0 or later (10.0 or later for concatenation) and the SQL is the call's second positional argument, which is exactly what EF checks. LC034 still reports on EF Core 7 and older, concatenation on EF Core 8 and 9, and a reordered named argument such as `ExecuteSqlRaw(parameters: args, sql: $"... {id}")`, which EF's analyzer does not see.

If your build excludes EF Core's analyzers or disables EF1002/EF1003, turn the deferral off so LC034 reports every call:

```ini
[*.cs]
dotnet_code_quality.LC034.defer_to_ef_analyzers = false
```

## Analyzer Logic

### ID: `LC034`
### Category: `Security`
### Severity: `Warning`

### Notes
The fixer is intentionally narrow. It appears only for direct interpolated-string calls with no additional raw SQL
parameters, where every interpolation hole is a supported core-library scalar type in an unambiguous single-statement
`UPDATE`, `DELETE`, or `INSERT` value position and the method-name rewrite keeps the SQL text and argument flow
semantically safe. It is not offered when an interpolation hole
appears inside SQL single quotes,
such as `'{name}'`; remove the SQL quotes manually before using `ExecuteSql(...)` or `ExecuteSqlAsync(...)` so EF can
parameterize the value correctly.

The fixer is also withheld for SQL structure that database parameters cannot represent, including table, column, and
stored-procedure identifiers such as `DELETE FROM {tableName}`, `WHERE {columnName} = 1`, or `EXEC {procedureName}`.
String, `char`, object, dynamic, and collection holes stay manual even after a comparison operator because the fixer
cannot prove whether they represent a scalar value or a structural fragment without changing raw interpolation
semantics. User-defined structs, enums, and generic type parameters
also stay manual because the fixer cannot prove provider mappings or formatting behaviour. Formatted or aligned holes,
adjacent holes, PostgreSQL dollar-quoted literals, provider comments such as MySQL `#`, batch separators such as `GO`,
separator-free non-DML batch commands, and other multi-statement SQL stay manual rather than reusing an earlier
value-position assumption. Framework-scalar
lookalikes declared by application source are not treated as framework types.
Provider-style backslash-escaped quotes are treated as
remaining inside the SQL literal. Interpolation inside bracketed, double-quoted, or backtick-delimited SQL identifiers
also remains manual, including identifiers that use doubled closing delimiters; apostrophes inside those identifiers do
not alter later SQL-string tracking. FixAll uses one shared action identity for mixed `ExecuteSqlRaw` and
`ExecuteSqlRawAsync` diagnostics while preserving the correct safe sibling for each call.
For `INSERT`, the fixer recognises direct scalar holes in complete `VALUES (...)` rows, including multiple parameters and
multiple rows, but keeps interpolated table names, column names, SQL expressions around a hole, and other ambiguous
INSERT shapes manual.
Keep the diagnostic and redesign the query around constant SQL, or select the structural fragment from a strict
application-owned allow-list before constructing the command.

No-hole interpolated strings and constant-only interpolations stay quiet because they do not embed runtime data into raw SQL.

`string.Format(...)`, `string.Concat(...)`, `StringBuilder`, and aliases that hide SQL construction are not auto-fixed by this rule. Those shapes need a manual rewrite to constant SQL plus parameters, and `LC037` reports them when they reach a raw SQL sink.

## Rule Boundary
- LC034 owns direct interpolated-string holes containing runtime data and direct non-constant `+` concatenation passed straight into `ExecuteSqlRaw(...)` or `ExecuteSqlRawAsync(...)`.
- LC034 requires the matched method to come from the EF Core namespace boundary (`Microsoft.EntityFrameworkCore` or a child namespace), not a same-named lookalike namespace.
- LC034 requires a `DatabaseFacade` receiver from the EF Core namespace, so same-named helpers on unrelated receiver types stay quiet.
- LC034 fires regardless of how the receiver is reached: the instance call `db.Database.ExecuteSqlRaw(...)` and the static-extension form `RelationalDatabaseFacadeExtensions.ExecuteSqlRaw(db.Database, ...)` both participate for sync and async; the safe siblings `ExecuteSql`/`ExecuteSqlAsync` stay quiet on every variant.
- LC018 owns direct interpolated and direct `+`-concatenated SQL passed to query APIs (`FromSqlRaw(...)` and `SqlQueryRaw<T>(...)`).
- LC037 owns broader constructed-SQL flows such as local aliases, `string.Format(...)`, `string.Concat(...)`, and `StringBuilder` before they reach `FromSqlRaw(...)`, `ExecuteSqlRaw(...)`, `ExecuteSqlRawAsync(...)`, or `SqlQueryRaw<T>(...)`.

### Boundary Examples

Direct `ExecuteSqlRaw` interpolation is `LC034` (EF1002 instead on EF Core 8+):

```csharp
await db.Database.ExecuteSqlRawAsync($"DELETE FROM Users WHERE Name = {name}");
```

Direct query API interpolation is `LC018`, not `LC034`:

```csharp
var users = db.Users.FromSqlRaw($"SELECT * FROM Users WHERE Name = {name}");
var names = db.Database.SqlQueryRaw<string>($"SELECT Name FROM Users WHERE Name = {name}");
```

Hidden construction is `LC037`:

```csharp
var sql = string.Format("DELETE FROM Users WHERE Name = '{0}'", name);
await db.Database.ExecuteSqlRawAsync(sql);
```
