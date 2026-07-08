---
layout: default
title: "Spec: LC010 - SaveChanges inside Loop"
---

# Spec: LC010 - SaveChanges inside Loop

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

It also does not report when the saved `DbContext` local is created inside the loop body and is not reassigned before the save, including delegate calls where that fresh context is passed directly or through a wrapper delegate parameter to the delegate parameter used by the save. That shape creates a fresh context per iteration, so moving the save outside the loop would be invalid and the per-iteration save is usually the intended unit-of-work boundary. Loop-local aliases to an outer context, contexts declared in a `for` initializer, locals reassigned before saving, delegate parameters reassigned on paths that can reach the save, wrapper delegate parameters reassigned on paths that can reach forwarding, and conditionally assigned delegates that can carry a captured loop-local context into later iterations still report because they can reuse the same context across iterations.

Retry loops that catch an exception and exit immediately after a successful save stay quiet when control cannot continue an enclosing per-item loop. A `break`-guarded retry nested inside an outer per-item loop still reports, and `break` statements that exit only a nested `switch` do not suppress LC010. A success path that `return`s from the method stays quiet.

The code fix is conservative. It only offers to move a terminal save out of a `do` loop, where the loop body executes at least once, and it skips loops containing control-flow statements such as `break`, `continue`, `return`, `throw`, `yield`, `try`, or `goto`. The fix also refuses when the `do` loop is itself nested inside another loop, because moving the save out of the inner `do` would leave it inside the outer loop and still trigger LC010, when the body holds more than one save call, or when the save is not the loop body's final statement. For `for`, `foreach`, and `while`, move the save manually after checking that per-item commits, retry boundaries, progress durability, and transaction semantics are not required.
