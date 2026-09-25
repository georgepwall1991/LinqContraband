---
layout: default
title: "LC058: TransactionScope without async flow spans an await"
description: "LC058 flags a TransactionScope created without TransactionScopeAsyncFlowOption.Enabled in async code, where the transaction is lost after an await."
---

# LC058: TransactionScope without async flow spans an await

## In Plain Terms

You put a "reserved" sign on your table and went to the bar. When you came back, a different waiter was on shift and had never heard of your reservation.

## Goal

Detect a `TransactionScope` created without `TransactionScopeAsyncFlowOption.Enabled` in an async method, lambda or local function when an `await` runs while the scope is still open.

## The Problem

By default a `TransactionScope` stores the ambient transaction (`Transaction.Current`) on the current thread. An `await` can resume the rest of the method on a different thread, and there the ambient transaction is gone:

- EF Core enlists in `Transaction.Current` when it opens the connection, so commands after the `await` can run outside the transaction and are not rolled back.
- Disposing the scope on the new thread throws `InvalidOperationException`: "A TransactionScope must be disposed on the same thread that it was created."

```csharp
// Violation: SaveChangesAsync may resume on another thread.
using var scope = new TransactionScope();
db.Orders.Add(order);
await db.SaveChangesAsync(ct);
scope.Complete();
```

The EF Core documentation on [using System.Transactions](https://learn.microsoft.com/ef/core/saving/transactions#using-systemtransactions) calls out `TransactionScopeAsyncFlowOption.Enabled` for async code.

## The Fix

Pass `TransactionScopeAsyncFlowOption.Enabled`, using the constructor overload that matches the arguments you already pass:

```csharp
using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
db.Orders.Add(order);
await db.SaveChangesAsync(ct);
scope.Complete();
```

```csharp
using var scope = new TransactionScope(
    TransactionScopeOption.Required,
    new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
    TransactionScopeAsyncFlowOption.Enabled);
```

## Analyzer Logic

### ID: `LC058`
### Category: `Reliability`
### Severity: `Warning`

1. Find `new System.Transactions.TransactionScope(...)` (including target-typed `new()`) whose constructor takes no `TransactionScopeAsyncFlowOption`, or is passed the constant `TransactionScopeAsyncFlowOption.Suppress`.
2. Require the scope to be created in an `async` method, lambda or local function.
3. Require the scope to be owned by a `using` statement or a `using` declaration.
4. Report when an `await`, `await foreach` or `await using` in the same method runs while the scope is alive: inside the `using` statement's body, or after the `using` declaration in the same block.

## When it stays quiet (non-goals)

- The scope passes `TransactionScopeAsyncFlowOption.Enabled`, or an option value that is not a constant.
- Synchronous code, including a non-async method that returns a `Task`.
- No `await` runs while the scope is alive: the awaits come before the scope, after the `using` statement, or after the block that holds the `using` declaration ends.
- An `await` inside a lambda or local function declared within the scope. That code runs when it is called, not as part of the scope.
- A scope that is not owned by a `using`, for example one disposed by hand in a `finally`. The rule does not track manual disposal.

## Code Fix

Adds `TransactionScopeAsyncFlowOption.Enabled` as the last argument when `TransactionScope` has an overload with the same parameters plus the async flow option: `()`, `(TransactionScopeOption)`, `(TransactionScopeOption, TimeSpan)`, `(TransactionScopeOption, TransactionOptions)`, `(Transaction)` and `(Transaction, TimeSpan)`. An explicit `Suppress` is replaced with `Enabled`. When the other arguments are named, the new one is named too (`asyncFlowOption:`). The fix adds `using System.Transactions;` when the file does not import it.

The overloads that take an `EnterpriseServicesInteropOption` have no async flow counterpart, so they get no fix. The fixer compiles the result and offers nothing when the rewrite would add an error.

## Test Cases

### Violations

```csharp
using (var scope = new TransactionScope()) { await db.SaveChangesAsync(ct); scope.Complete(); }
using var scope = new TransactionScope(TransactionScopeOption.Required, options); await db.SaveChangesAsync(ct);
using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Suppress); await db.SaveChangesAsync(ct);
```

### Valid

```csharp
using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled); await db.SaveChangesAsync(ct);
using (var scope = new TransactionScope()) { db.SaveChanges(); scope.Complete(); }
```
