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
