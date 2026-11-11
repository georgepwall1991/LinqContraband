---
layout: default
title: "LC010: SaveChanges Inside Loop"
description: "LC010 flags SaveChanges or SaveChangesAsync inside a loop, which costs one database round trip per item instead of one batched save."
---

# LC010: SaveChanges Inside Loop

## In Plain Terms

Imagine mailing 100 letters. Instead of putting them all in the mailbox at
once, you put one in, wait for the mailman to pick it up, then put the next one in. It takes 100 days to mail your
invites!

## Goal
Detect usage of `SaveChanges()` or `SaveChangesAsync()` inside a loop.

## The Problem
Every call to `SaveChanges()` opens a new database transaction. Calling it inside a loop results in many small transactions, which is extremely slow due to the network and disk overhead of committing each change individually.

### Example Violation
```csharp
foreach (var user in users)
{
    user.Active = true;
    // Violation: 100 users = 100 transactions!
    db.SaveChanges();
}
```

### The Fix
Call `SaveChanges()` once after the loop to batch all changes into a single, efficient transaction.

```csharp
foreach (var user in users)
{
    user.Active = true;
}
// Correct: 100 users = 1 transaction
db.SaveChanges();
```

## Analyzer Logic

### ID: `LC010`
### Category: `Performance`
### Severity: `Warning`

### Notes
LC010 reports direct `SaveChanges()`/`SaveChangesAsync()` calls inside loops when the save call and loop are part of the same executable body. It also reports a local function body when that local function is invoked from a loop in the containing executable body, and local delegate targets when the delegate is assigned, subscribed with `+=`, or preserved by a self-combining assignment such as `save = save + other` to a save-containing lambda, local function, conditional delegate branch, local delegate alias, or `DbContext.SaveChanges`/`SaveChangesAsync` method group before being invoked from a loop or assigned later in the same loop body and carried into a subsequent iteration, including opposite-branch loop-carried assignments, wrapper delegate/local-function/callback-helper assignments after invocation, `?.Invoke()`, setup helpers reached through live local functions or wrapper setup helpers, local invoker callback helpers, and proven outer-delegate call chains.

It does not report saves inside a local function or lambda that is merely declared inside a loop, because that delegate is not necessarily executed once per iteration. Delegate calls outside loops, matching delegate removals with `-=`, branch-exclusive delegate assignments with stable guards, branch-exiting delegate assignments with loop-stable guards, branch-exclusive conditional initializer arms with stable guards, negated-guard delegate paths, same-path or called-helper delegate overwrites, and delegate locals reassigned before the loop call stay quiet. Loop-variant branch-exiting or opposite-branch assignments can still report when the saved delegate can be carried into a later iteration, including when a loop-called wrapper or callback helper invokes the current delegate and then assigns the save delegate for the next iteration. Duplicate multicast subscriptions still report when a single matching removal leaves another save handler in the invocation list. Lambdas nested inside a local function or another delegate remain quiet unless the save itself is directly inside a proven loop or the lambda is reached through a proven local delegate, outer-delegate, or local-function loop call.

It also does not report when the saved `DbContext` local is created inside the loop body and is not reassigned before the save, including delegate calls where that fresh context is passed directly or through a wrapper delegate parameter to the delegate parameter used by the save. A context counts as created inside the loop body when its initializer is `new TContext(...)`, `IDbContextFactory<TContext>.CreateDbContext()` or `await factory.CreateDbContextAsync()` with or without `.ConfigureAwait(...)` (the interface member or a class's implementation of it, including one inherited from a base class), a call to a non-virtual same-project method whose whole body is `new TContext(...)` (a wrapper object returned through a user-defined conversion does not count), or `scope.ServiceProvider.GetRequiredService<TContext>()` / `GetService<TContext>()` on a scope local created in the same loop body by the dependency-injection `CreateScope()` or `CreateAsyncScope()` (or a class's implementation of `IServiceScopeFactory.CreateScope()`) and not reassigned. LC010 assumes the default scoped lifetime for a context resolved from a scope; a singleton `DbContext` registration is reported by [LC030](/LinqContraband/LC030_DbContextInSingleton.html). That shape creates a fresh context per iteration, so moving the save outside the loop would be invalid and the per-iteration save is usually the intended unit-of-work boundary. Loop-local aliases to an outer context, contexts declared in a `for` initializer, locals reassigned before saving (including `db ??= fallback` and deconstruction, directly or in a local function called first, at any depth), delegate parameters reassigned on paths that can reach the save, wrapper delegate parameters reassigned on paths that can reach forwarding, and conditionally assigned delegates that can carry a captured loop-local context into later iterations still report because they can reuse the same context across iterations. A factory-created context or a scope declared outside the loop, a context resolved from the root or an outer service provider, helpers that return a field or are virtual, and same-named project methods that are not the factory or DI scope API also still report.

LC010 also reports a save directly inside a lambda that an in-memory operator runs once per element: `items.ForEach(item => { ...; db.SaveChanges(); })`, `items.Select(item => db.SaveChanges())`, `await Task.WhenAll(items.Select(item => db.SaveChangesAsync()))` and the body of `Parallel.ForEach`. The operators are the ones LC007 treats as per-element (listed on the LC007 page). It stays quiet when each element saves a context of its own (a context the lambda declares, or the lambda's own parameter, as in `contexts.ForEach(c => c.SaveChanges())`), when the operator walks `Chunk(...)` batches, and for lambdas that are not per element, such as `Task.Run(() => db.SaveChanges())` or the result selector of `Aggregate`. The fixer leaves a save inside a lambda alone, because moving it out would change what each element sees.

LC010 also follows the call into a helper that saves. A repository whose `Add` saves, or a unit of work whose `Commit` runs for every item, is the same N+1 write with the save one call away:

```csharp
foreach (var line in lines)
{
    _auditLog.Add(new AuditEntry(line));   // LC010: 'Add' calls 'SaveChanges', so this saves once per item
}

public class AuditLog
{
    public void Add(AuditEntry entry)
    {
        _db.Audit.Add(entry);
        _db.SaveChanges();
    }
}
```

In the IDE and in a build the helper must be declared in the project being analyzed; the [scanner](/LinqContraband/ef-core-query-scanner/) builds the whole solution and also follows a per-item call into a helper, or an interface implementation, that saves in another project. Helpers are followed three calls deep, and a call through an interface or to an `abstract` or `virtual` method is followed when every implementation in the project saves (the same rules as [LC007](/LinqContraband/LC007_NPlusOneLooper.html) uses for queries). The report is on the call in the loop, or in a per-element lambda, and the loop exemptions on this page apply to it. It stays quiet when the helper saves a context it declares itself (`using var db = factory.CreateDbContext(); ...; db.SaveChanges();`), when the object the helper is called on is created, declared or iterated per item (`new Importer(db).Run(item)`, `handler.Save()` on the loop variable), when a context declared per item is passed in, and when one implementation does not save. The fixer does not move a helper call.

A save once per batch stays quiet, because saving every few hundred rows is how large jobs keep the change tracker small. That covers a `foreach` over `Chunk(...)` (`foreach (var batch in users.Chunk(1000)) { db.Users.AddRange(batch); db.SaveChanges(); }`) and a `while`, `do` or `for` loop whose query LC007 treats as one query per batch: keyset or `Skip(page * size).Take(size)` paging, drain loops and polling loops, the same loops LC007 exempts. A batch loop inside an outer per-item loop still reports, and so does a counted loop that walks an array (`i < ids.Length`).

A save every N items stays quiet for the same reason, when it is written with a counter or a buffer that lives across the loop's passes:

```csharp
var pending = 0;
foreach (var row in rows)
{
    db.Audit.Add(new AuditEntry(row));
    if (++pending % 1000 == 0)
        await db.SaveChangesAsync(ct);     // quiet: one save per thousand rows
}

await db.SaveChangesAsync(ct);             // the rest
```

LC010 accepts three guards, on the save itself or on a call to a helper that saves:

- `counter % n == k`, where the counter is an integer local (or the `for` loop's own variable) that the loop only advances by one: `++pending % 1000 == 0`, `(i + 1) % batchSize == 0`, `i > 0 && i % 500 == 0`. A constant `n` must be greater than 1.
- `counter >= n` (or `>`, `==`) with `counter = 0;` in the same branch: after the reset the guard needs `n` more before it holds again, so the counter may count rows instead of passes (`rows += group.Count`).
- `buffer.Count >= n` with `buffer.Clear();` (or `buffer = new List<T>();`) in the same branch.

It still reports a guard that reads the item (`if (order.Id % 2 == 0)`, `if (group.Count >= 10)` on the loop variable), a threshold without a reset (it holds on every pass once it is reached), `% 1`, `!=`, a guard joined with `||`, a counter that another loop steps or that changes by anything but one under a modulo guard, and a counter or buffer declared inside an outer per-item loop, which starts again for every outer item. LC010 does not check that the counter is stepped on every pass, and it does not read a guard-clause form (`if (++pending % 1000 != 0) continue;`) or a save inside a local function, a delegate or a per-element lambda.

Retry loops that catch an exception and exit immediately after a successful save stay quiet when control cannot continue an enclosing per-item loop. A `break`-guarded retry nested inside an outer per-item loop still reports, and `break` statements that exit only a nested `switch` do not suppress LC010. A success path that `return`s from the method stays quiet, whether the `return` follows the save or returns it (`try { return await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) when (attempt < 3) { }`).

A save that cannot run twice in the loop stays quiet: a `foreach` over a LanguageExt `Option<T>` (zero or one value) or over the sequence form of an `Option<T>` or `Either<L, R>` (`maybeList.RightToSeq()`), a loop body that always ends in `return`, `throw` or `break`, and a save followed in its block by an exit (`await db.SaveChangesAsync(); return;`). A `continue` before the exit, an exit inside a `catch`, or an outer per-item loop still reports.

A loop over a list whose length the source code fixes stays quiet too, because the number of saves does not grow with the data: `foreach (var role in new[] { "Admin", "User" })`, a collection built from an initializer alone, the values or names of an enum, or a local whose only assignment is such an array or enum call ([LC007](/LinqContraband/LC007_NPlusOneLooper.html) uses the same rule). An outer per-item loop around it still reports, and so does a list held in a local, which can grow after it is declared.

The code fix is conservative. It only offers to move a terminal save out of a `do` loop, where the loop body executes at least once, and it skips loops containing control-flow statements such as `break`, `continue`, `return`, `throw`, `yield`, `try`, or `goto`. The fix also refuses when the `do` loop is itself nested inside another loop, because moving the save out of the inner `do` would leave it inside the outer loop and still trigger LC010, when the body holds more than one save call, or when the save is not the loop body's final statement. For `for`, `foreach`, and `while`, move the save manually after checking that per-item commits, retry boundaries, progress durability, and transaction semantics are not required.
