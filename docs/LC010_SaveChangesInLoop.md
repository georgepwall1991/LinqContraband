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
LC010 reports direct `SaveChanges()`/`SaveChangesAsync()` calls inside loops when the save call and loop are part of the same executable body. It also reports a local function body when that local function is invoked from a loop in the containing executable body.

It does not report saves inside a local function or lambda that is merely declared inside a loop, because that delegate is not necessarily executed once per iteration. Lambdas nested inside a local function remain quiet unless the save itself is directly inside a proven loop.

Retry loops that catch an exception and exit the loop immediately after a successful save with `break` or `return` stay quiet, because they represent one intended commit attempt rather than a per-item save pattern.

The code fix is conservative. It only offers to move a terminal save out of a `do` loop, where the loop body executes at least once, and it skips loops containing control-flow statements such as `break`, `continue`, `return`, `throw`, `yield`, `try`, or `goto`. For `for`, `foreach`, and `while`, move the save manually after checking that per-item commits, retry boundaries, progress durability, and transaction semantics are not required.
