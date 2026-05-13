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

## Analyzer Logic

### ID: `LC039`
### Category: `Reliability`
### Severity: `Info`

### Notes
The rule suppresses obvious EF Core transaction-boundary cases, repeated saves inside the same explicit transaction `using` block, repeated saves inside a C# 8+ `using`/`await using` local declaration of an EF Core transaction, mutually exclusive `if`/`else` branches, and mutually exclusive `switch` sections, then reports on a per-context basis within the same executable root.

Only EF Core transaction APIs on `DatabaseFacade` or `IDbContextTransaction`-style receivers count as boundaries. Unrelated helper methods named `Commit`, `Rollback`, or similar do not suppress the diagnostic. A `using` declaration of an unrelated disposable (for example a `MemoryStream`) does not suppress the diagnostic, and a transaction `using` declaration introduced after the first save does not retroactively cover saves that preceded it.
