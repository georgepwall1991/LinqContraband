# Spec: LC043 - Async Enumerable Buffering

## Goal
Detect immediate buffering of an `IAsyncEnumerable<T>` into a list or array before a single `foreach`.

## The Problem
Buffering an async stream into memory and then looping exactly once throws away streaming behavior for no benefit.

### Example Violation
```csharp
var users = await stream.ToListAsync();
foreach (var user in users)
{
    Console.WriteLine(user.Name);
}
```

### The Fix
Stream directly with `await foreach`.

```csharp
await foreach (var user in stream)
{
    Console.WriteLine(user.Name);
}
```

## Analyzer Logic

### ID: `LC043`
### Category: `Performance`
### Severity: `Info`

### Notes
This v1 rule is intentionally narrow. It reports only immediate buffer-then-loop patterns that are safe to rewrite to `await foreach`. Calls with buffer-method arguments, such as cancellation tokens, are left alone so the fixer does not drop behavior.
