---
layout: default
title: "LC059: Connection owned by the DbContext is disposed"
description: "LC059 flags code that disposes the connection from EF Core GetDbConnection(), which the DbContext owns, breaking later queries and SaveChanges."
---

# LC059: Connection owned by the DbContext is disposed

## In Plain Terms

You borrowed the restaurant's only corkscrew and threw it away when you were done. The next table that orders wine is out of luck.

## Goal

Detect code that disposes the `DbConnection` returned by `DatabaseFacade.GetDbConnection()`.

## The Problem

`context.Database.GetDbConnection()` hands back the connection the `DbContext` itself uses. The context created it and disposes it when the context is disposed. Wrapping it in `using` or calling `Dispose()` tears it down underneath EF Core: providers such as SqlClient reset the connection string on dispose, so the next query or `SaveChanges` on the same context fails, and with `AddDbContextPool` the broken connection can reach the next request that leases the context. See [dotnet/efcore#12992](https://github.com/dotnet/efcore/issues/12992) and the [`GetDbConnection` API documentation](https://learn.microsoft.com/dotnet/api/microsoft.entityframeworkcore.relationaldatabasefacadeextensions.getdbconnection), which says the connection should not be disposed.

```csharp
// Violation: disposes the context's connection.
await using var connection = db.Database.GetDbConnection();
await connection.OpenAsync(ct);
await using var command = connection.CreateCommand();
command.CommandText = "SELECT COUNT(*) FROM Users";
var count = await command.ExecuteScalarAsync(ct);
```

## The Fix

Dispose what you create (the command, a reader) and close the connection if your code opened it. Leave the connection itself to the context:

```csharp
var connection = db.Database.GetDbConnection();
await connection.OpenAsync(ct);
try
{
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM Users";
    var count = await command.ExecuteScalarAsync(ct);
}
finally
{
    await connection.CloseAsync();
}
```

`db.Database.OpenConnectionAsync()` and `CloseConnectionAsync()` let EF Core track the open state for you.

## Analyzer Logic

### ID: `LC059`
### Category: `Reliability`
### Severity: `Warning`

Reports when the connection from `GetDbConnection()`, directly, through a cast, or through a local whose only assignment is that call, is:

1. The resource of a `using` or `await using` declaration: `using var connection = db.Database.GetDbConnection();`.
2. The resource of a `using` or `await using` statement: `using (var connection = ...)`, `using (connection)`, `using (db.Database.GetDbConnection())`.
3. The receiver of `Dispose()` or `DisposeAsync()`.

## When it stays quiet (non-goals)

- Disposing something created from the connection: `using var command = db.Database.GetDbConnection().CreateCommand();`.
- `Close()` and `CloseAsync()`. Closing a connection the code opened is correct.
- Connections the code creates itself, such as `new SqlConnection(...)` or a factory call.
- A local that can hold another connection, because it is assigned more than once.
- Conditional disposal through `?.`.

## Code Fix

Removes the disposal and leaves it to the `DbContext`:

- `using var connection = db.Database.GetDbConnection();` and `await using var ...` become `var connection = db.Database.GetDbConnection();`.
- A `using` or `await using` statement becomes a plain block that starts with the declaration, so the local keeps its scope: `using (var c = ...) { ... }` becomes `{ var c = ...; ... }`. A statement without a declaration (`using (connection) { ... }`) keeps just its body.
- A `connection.Dispose();` or `await connection.DisposeAsync();` statement is removed.

A `using` that declares another resource as well (`using DbConnection a = ..., b = ...;`) and a disposal that is not a statement of its own in a block (the body of an `if` without braces, a lambda body) get no fix. The fixer compiles the result and offers nothing when the rewrite would add an error.

## Test Cases

### Violations

```csharp
using var connection = db.Database.GetDbConnection();
await using (var connection = db.Database.GetDbConnection()) { await connection.OpenAsync(ct); }
var connection = db.Database.GetDbConnection(); connection.Open(); connection.Dispose();
```

### Valid

```csharp
using var command = db.Database.GetDbConnection().CreateCommand();
var connection = db.Database.GetDbConnection(); await connection.OpenAsync(ct); await connection.CloseAsync();
using var owned = new SqlConnection(connectionString);
```
