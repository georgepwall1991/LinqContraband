---
layout: default
title: "LC043: Async Enumerable Buffering"
description: "LC043 flags an IAsyncEnumerable buffered with ToListAsync or ToArrayAsync only to loop over it once. Use await foreach to stream instead."
---

# LC043: Async Enumerable Buffering

## In Plain Terms

Imagine waiting for every toy to arrive in one giant pile before you start
playing, even though you could play with each toy as soon as it shows up.

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
This v1 rule is intentionally narrow. It reports only immediate buffer-then-loop patterns that are safe to rewrite to `await foreach`. The buffered call must come from a proven `IAsyncEnumerable<T>` source, so custom non-stream helpers named `ToListAsync` or `ToArrayAsync` stay quiet. Calls with buffer-method arguments, such as cancellation tokens, are left alone so the fixer does not drop behavior. Buffers captured by nested lambdas or local functions are also left alone because removing the local would break that captured use.

The code fix works on any receiver, including a chained one such as `await db.Users.AsAsyncEnumerable().ToListAsync()`, which becomes `await foreach (var user in db.Users.AsAsyncEnumerable())`.
