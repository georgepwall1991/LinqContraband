---
layout: default
title: "LC054: Migrate called inside a user transaction"
description: "LC054 flags EF Core Database.Migrate() and MigrateAsync() calls inside a transaction begun on the same context, which EF Core 9 and later reject at startup."
---

# LC054: Migrate called inside a user transaction

## In Plain Terms

You booked the meeting room for the whole afternoon, and now the building manager can't get in to do the renovation they were scheduled for. EF Core 9 wants the room to itself while it applies migrations.

## Goal

Detect `Database.Migrate()` and `Database.MigrateAsync()` calls that run while a transaction the code started on the same `DbContext` is still open.

## The Problem

Before EF Core 9, wrapping migrations in an explicit transaction inside an execution strategy was the documented way to make them resilient. From EF Core 9, `Migrate` starts its own transaction, runs its own execution strategy, and takes a database lock so that two app instances cannot migrate at once. When the context already has a transaction, EF Core raises `MigrationsUserTransactionWarning`, which is configured to throw by default. With a retrying execution strategy it throws `NotSupportedException` whatever the warning setting. Either way the app fails at startup, often only after the EF Core upgrade reaches production.

```csharp
// Violation: EF Core 9+ throws when MigrateAsync runs inside this transaction.
await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
{
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await db.Database.MigrateAsync(ct);
    await tx.CommitAsync(ct);
});
```

## The Fix

Call `Migrate` on its own. EF Core manages the transaction and the retries itself:

```csharp
await db.Database.MigrateAsync(ct);
```

The execution-strategy wrapper is no longer needed either, but leaving it is harmless.

## Analyzer Logic

### ID: `LC054`
### Category: `Reliability`
### Severity: `Warning`

1. Find `RelationalDatabaseFacadeExtensions.Migrate` / `MigrateAsync` on `ctx.Database`, where `ctx` is a local, parameter, field, auto-property, or `this`.
2. Walk up from the call, within the same method, lambda, or local function, looking for a transaction begun on the same context: a `using` or plain local initialized from `ctx.Database.BeginTransaction(...)` / `BeginTransactionAsync(...)`, an assignment of one to a local, a bare `ctx.Database.BeginTransaction();` statement, or a `using (...)` statement that owns one.
3. Report when nothing between the transaction and `Migrate` could have ended it. When `Migrate` sits in a loop inside the transaction's scope, the whole loop body counts as "between".

The rule runs only when the project references EF Core 9 or later, detected by `RelationalEventId.MigrationsUserTransactionWarning`. On EF Core 8 the old pattern still works.

## When it stays quiet (non-goals)

- The transaction is committed, rolled back, disposed, passed to other code, or read through `Database.CurrentTransaction` before `Migrate` runs, or `CommitTransaction` / `RollbackTransaction` / `UseTransaction` runs on any context in between.
- The transaction was begun on a different context, on a computed property that may return a new context on each read, or the context local is reassigned before `Migrate`.
- The transaction is begun conditionally (`cond ? ctx.Database.BeginTransaction() : null`, or inside an `if`).
- `Migrate` runs in a lambda or local function and the transaction is outside it, since the delegate may run after the transaction ends.
- `TransactionScope`. EF Core suppresses the ambient transaction while it migrates, so it does not trigger the error.
- Transactions attached with `Database.UseTransaction(dbTransaction)`. They also trigger the error but are not tracked.
- The project downgrades the warning anywhere with `Ignore(RelationalEventId.MigrationsUserTransactionWarning)` or `Log(...)`.

## Code Fix

When the transaction covers nothing but `Migrate`, the fixer removes it:

- A begin statement directly followed by the `Migrate` statement, and then either the end of the block or a single `Commit` / `CommitAsync` (or `Database.CommitTransaction()` for a bare begin): the begin and the commit are removed.
- A `using (...)` statement whose body is the `Migrate` statement and an optional commit: the `using` is replaced by the `Migrate` statement.

Other work in the same transaction (seeding, a second `SaveChanges`) would lose its atomicity, so those cases are reported without a fix. So are transactions assigned to a local declared earlier, where removing the assignment would leave the declaration unused.

## Test Cases

### Violations

```csharp
using var tx = db.Database.BeginTransaction();
db.Database.Migrate();
tx.Commit();

using (db.Database.BeginTransaction())
{
    db.Database.Migrate();
}

using var tx = db.Database.BeginTransaction();
for (var i = 0; i < 2; i++)
{
    db.Database.Migrate();
}
tx.Commit();
```

### Valid

```csharp
await db.Database.MigrateAsync(ct);

using var tx = db.Database.BeginTransaction();
tx.Commit();
db.Database.Migrate();

db.Database.BeginTransaction();
db.Database.RollbackTransaction();
db.Database.Migrate();

using var otherTx = reportingDb.Database.BeginTransaction();
db.Database.Migrate();
```
