---
layout: default
title: "Spec: LC039 - Nested SaveChanges"
---

# Spec: LC039 - Nested SaveChanges

## Goal
Detect repeated `SaveChanges()` / `SaveChangesAsync()` calls on the same context in one method scope.

## The Problem
Multiple saves in one executable root often mean unnecessary round-trips, fragmented transactions, or partial writes that should have been batched.

### Example Violation
```csharp
db.SaveChanges();
db.SaveChanges();
```

### Safer Shape
Prefer one unit of work: make all tracked changes first, then save once.

```csharp
user.Name = name;
order.Status = OrderStatus.Paid;

db.SaveChanges();
```

If two saves are deliberately separated because the first write must be flushed before the second step, make that boundary visible with an EF Core transaction or split the work into separate executable roots.

```csharp
using var transaction = db.Database.BeginTransaction();

db.SaveChanges();
AuditAfterFirstCommit();
db.SaveChanges();

transaction.Commit();
```

The transaction is not a performance trick; it documents that the two saves are part of a deliberate transactional sequence rather than an accidental batching miss.

## Analyzer Logic

### ID: `LC039`
### Category: `Reliability`
### Severity: `Info`

### Notes
The rule suppresses obvious EF Core transaction-boundary cases, repeated saves inside the same explicit transaction `using` block, repeated saves inside a C# 8+ `using`/`await using` local declaration of an EF Core transaction, mutually exclusive `if`/`else` branches, mutually exclusive `switch` sections and switch-expression result arms, and a `try` block versus a `catch` clause (a `catch` save is a compensating/retry save, not a batchable repeat — but a `finally` save still counts because it always runs), then reports on a per-context basis within the same executable root.

Only EF Core transaction APIs on `DatabaseFacade` or `IDbContextTransaction`-style receivers count as boundaries. Unrelated helper methods named `Commit`, `Rollback`, or similar do not suppress the diagnostic. A `using` declaration of an unrelated disposable (for example a `MemoryStream`) does not suppress the diagnostic, and a transaction `using` declaration introduced after the first save does not retroactively cover saves that preceded it.

## Rule Boundary
- LC039 is scoped to repeated `SaveChanges()` / `SaveChangesAsync()` calls on the same provable `DbContext` receiver inside one executable root.
- Separate context instances do not report; saving `db1` and `db2` is treated as two different units of work.
- Nested local functions, lambdas, and other executable roots are analysed independently. A save in the outer method and a save in an inner local function are not treated as one repeated-save sequence.
- Independent `if` statements can still report because both branches may execute in one call. Mutually exclusive `if`/`else`, `else if`, `switch` sections, and switch-expression result arms stay quiet. A save in a switch-expression pattern or `when` guard can still report with a later arm because guards may run before matching continues.
- `try` versus `catch` saves stay quiet because the catch save is a retry/compensation path after the try save failed. `finally` saves still report because the finally block runs after the try path.
- Transaction boundaries are recognised only when the invocation is on EF Core's `DatabaseFacade` or an EF Core transaction type. Lookalike methods on application services are intentionally ignored.
- This rule has no code fix. Whether to batch, split the method, introduce an explicit transaction, or keep the repeated save depends on the business invariant protected by the earlier commit.

### Boundary Examples

Independent branches can both run, so the second save reports:

```csharp
if (saveUser)
{
    db.SaveChanges();
}

if (saveAudit)
{
    db.SaveChanges();
}
```

Mutually exclusive branches stay quiet:

```csharp
if (saveUser)
{
    db.SaveChanges();
}
else
{
    db.SaveChanges();
}
```

A retry/compensation save is allowed:

```csharp
try
{
    db.SaveChanges();
}
catch
{
    db.SaveChanges();
}
```

A finally save always runs, so it still reports:

```csharp
try
{
    db.SaveChanges();
}
finally
{
    db.SaveChanges();
}
```
