# Spec: LC036 - DbContext Captured Across Threads

## Goal
Detect a single `DbContext` captured into multi-threaded delegates.

## The Problem
`DbContext` is not thread-safe. Capturing the same instance into `Task.Run(...)`, `Task.Factory.StartNew(...)`, `Parallel.ForEach(...)`, thread-pool work, `Thread`, or timer callbacks can lead to race conditions and undefined behavior.

### Example Violation
```csharp
Task.Run(() => db.Users.ToList());
Task.Factory.StartNew(() => db.SaveChanges());
Parallel.ForEach(ids, _ => db.Users.Count());
new Thread(() => db.SaveChanges()).Start();
new Timer(_ => db.SaveChanges(), null, 0, 1000);

int Work() => db.SaveChanges();
Task.Run(Work);
```

### Safer Shape
Create a separate context inside each background delegate, create a scope inside the callback, or use `IDbContextFactory<TContext>`.

```csharp
Task.Run(() =>
{
    using var db = factory.CreateDbContext();
    return db.Users.Count();
});
```

## Analyzer Logic

### ID: `LC036`
### Category: `Reliability`
### Severity: `Warning`

### Notes
This rule is advisory only and stays silent when the delegate creates its own context, obtains one from a scope created inside the callback, or captures only scalar/materialized values. Capturing a `DbContext` field, property, local, or parameter from outside the callback is unsafe because the work can run after or concurrently with the original scope.

LC036 also inspects local functions passed directly as thread-work callbacks. It does not broadly inspect arbitrary method groups because method ownership, dependency lifetime, and call dispatch are harder to prove without creating noisy diagnostics.
