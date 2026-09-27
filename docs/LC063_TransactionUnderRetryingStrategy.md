---
layout: default
title: "LC063: User transaction under a retrying execution strategy"
description: "LC063 flags BeginTransaction on an EF Core context set up with EnableRetryOnFailure, which throws unless the transaction runs in the execution strategy."
---

# LC063: User transaction under a retrying execution strategy

## In Plain Terms

The courier promises to try the delivery again if the first attempt fails, but only if the parcel is sealed. You hand over half a parcel and keep the rest in your pocket, so the courier refuses the job.

## Goal

Detect a transaction the code starts itself (`BeginTransaction`, `BeginTransactionAsync`, `UseTransaction`) on a `DbContext` that is configured with a retrying execution strategy, when the transaction does not run through that strategy.

## The Problem

Connection resiliency (`EnableRetryOnFailure()` on the SQL Server, Azure SQL, PostgreSQL, MySQL or Oracle provider, or a custom strategy derived from `ExecutionStrategy`) retries each operation on its own when a transient error occurs. It cannot retry half of a transaction, so EF Core refuses to run operations inside a transaction the code started:

```csharp
// services.AddDbContext<AppDb>(o => o.UseSqlServer(cs, sql => sql.EnableRetryOnFailure()));

await using var tx = await db.Database.BeginTransactionAsync();
db.Orders.Add(order);
await db.SaveChangesAsync();   // InvalidOperationException
await tx.CommitAsync();
```

> The configured execution strategy 'SqlServerRetryingExecutionStrategy' does not support user-initiated transactions. Use the execution strategy returned by 'DbContext.Database.CreateExecutionStrategy()' to execute all the operations in the transaction as a retriable unit.

Retries are often only enabled outside development, so the exception tends to show up first in production. See the EF Core documentation on [execution strategies and transactions](https://learn.microsoft.com/ef/core/miscellaneous/connection-resiliency#execution-strategies-and-transactions) and [dotnet/efcore#34825](https://github.com/dotnet/efcore/issues/34825).

## The Fix

Run the whole transaction through the context's execution strategy, so a transient failure retries all of it:

```csharp
var strategy = db.Database.CreateExecutionStrategy();
await strategy.ExecuteAsync(async () =>
{
    await using var tx = await db.Database.BeginTransactionAsync();
    db.Orders.Add(order);
    await db.SaveChangesAsync();
    await tx.CommitAsync();
});
```

The code inside the delegate can run more than once, so keep work that must happen once (sending an email, calling another service) outside it, and make sure the delegate can start again after a failure. If a failure during commit can leave the outcome unknown, `ExecuteInTransactionAsync` with a verification delegate checks whether the commit succeeded before retrying.

## Analyzer Logic

### ID: `LC063`
### Category: `Reliability`
### Severity: `Warning`

1. Find the retrying configurations in the project: `EnableRetryOnFailure(...)` on a provider options builder (any type whose name ends in `OptionsBuilder`), and `ExecutionStrategy(...)` on a relational options builder whose factory creates a type derived from EF Core's `ExecutionStrategy` base class.
2. Tie each configuration to a context type: the `TContext` of an enclosing `AddDbContext`, `AddDbContextPool`, `AddDbContextFactory` or `AddPooledDbContextFactory` call (the implementation type when there are two), of a `DbContextOptionsBuilder<TContext>` chain, or the context whose `OnConfiguring` override holds the call. A configuration that cannot be tied to one (for example in a helper shared by several registrations) counts only when the project declares exactly one non-abstract context type.
3. Report `Database.BeginTransaction(...)`, `Database.BeginTransactionAsync(...)`, `Database.UseTransaction(transaction)` and `UseTransactionAsync(transaction)` on a context whose static type is, or derives from, a configured context.

## When it stays quiet (non-goals)

- The transaction starts inside a delegate passed to the strategy's `Execute`, `ExecuteAsync`, `ExecuteInTransaction` or `ExecuteInTransactionAsync`, or to a method in the project that mentions an execution strategy (such as a `ResilientTransaction` helper).
- The transaction starts in a method or local function that some delegate passed to an execution strategy calls, or passes as a method group. The rule does not check that every caller does this.
- The transaction starts inside a lambda the code stores (`Func<Task> work = async () => ...`), which may be handed to a strategy later.
- Retries are turned off: `EnableRetryOnFailure(0)`, `maxRetryCount: 0`, a strategy created with a constant retry count of 0, or a strategy that does not derive from `ExecutionStrategy` (such as `NonRetryingExecutionStrategy`).
- The context's static type is `DbContext`, an interface, or a context with no retrying configuration in this project. Configuration in another project, and retries switched on by a library (for example .NET Aspire's `AddSqlServerDbContext`), are not seen.
- `UseTransaction(null)`, which clears the transaction.
- `TransactionScope` is not tracked.

## Code Fix

For the simple shape, the fix moves the transaction into the execution strategy. When the transaction is the single local of a `using` or `await using` declaration directly inside a block, the declaration and the rest of the block move into the delegate. When it is owned by a `using` statement directly inside a block, that statement moves:

```csharp
var strategy = db.Database.CreateExecutionStrategy();
await strategy.ExecuteAsync(async () =>
{
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
});
```

The delegate is `async` and awaited when the moved code awaits; otherwise the fix uses `strategy.Execute(() => { ... })`. Comments above the transaction stay above the strategy. The fix adds `using Microsoft.EntityFrameworkCore;` when the file needs it for `Execute`/`ExecuteAsync`, and picks another name when `strategy` is taken.

There is no fix when the moved code contains `return`, `yield`, `goto` or a label, when the context is not a local, parameter, field, auto-property or `this`, for `UseTransaction`, for the static `RelationalDatabaseFacadeExtensions.BeginTransaction(...)` form, for a transaction that is not owned by a using, or for a using with several resources. The fixer compiles the result and offers nothing when the rewrite would add an error, which also covers `ref`/`out` parameters used in the moved code, `break`/`continue` out of the block, and locals that are no longer definitely assigned after it.

## Test Cases

### Violations

```csharp
// With AddDbContext<AppDb>(o => o.UseSqlServer(cs, sql => sql.EnableRetryOnFailure()))
await using var tx = await db.Database.BeginTransactionAsync(ct); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
using (var tx = db.Database.BeginTransaction()) { db.SaveChanges(); tx.Commit(); }
db.Database.UseTransaction(externalTransaction); db.SaveChanges();
```

### Valid

```csharp
await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
{
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
});

// With EnableRetryOnFailure(0), or on a context without retries
using var tx = db.Database.BeginTransaction(); db.SaveChanges(); tx.Commit();
```
