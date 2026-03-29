# Spec: LC036 - DbContext Captured Across Threads

## Goal
Detect a single `DbContext` captured into multi-threaded delegates.

## The Problem
`DbContext` is not thread-safe. Capturing the same instance into `Task.Run(...)`, `Parallel.ForEach(...)`, or thread-pool work can lead to race conditions and undefined behavior.

### Example Violation
```csharp
Task.Run(() => db.Users.ToList());
Parallel.ForEach(ids, _ => db.Users.Count());
```

### Safer Shape
Create a separate context per background delegate, or use `IDbContextFactory<TContext>`.

## Analyzer Logic

### ID: `LC036`
### Category: `Reliability`
### Severity: `Warning`

### Notes
This rule is advisory only and stays silent when the delegate creates its own context instead of capturing one from the outer scope.
