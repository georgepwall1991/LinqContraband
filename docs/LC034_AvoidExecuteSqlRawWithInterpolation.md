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

## Analyzer Logic

### ID: `LC034`
### Category: `Security`
### Severity: `Warning`

### Notes
The fixer is intentionally narrow. It appears only for direct interpolated-string calls with no additional raw SQL
parameters, where the method-name rewrite keeps the SQL text and argument flow semantically safe. It is not offered when
an interpolation hole appears inside SQL single quotes, such as `'{name}'`; remove the SQL quotes manually before using
`ExecuteSql(...)` or `ExecuteSqlAsync(...)` so EF can parameterize the value correctly.

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

Direct `ExecuteSqlRaw` interpolation is `LC034`:

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
