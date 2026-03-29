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
The rule suppresses obvious transaction-boundary cases and reports on a per-context basis within the same executable root.
