---
layout: default
title: "LC026: Missing CancellationToken in Async Call"
description: "LC026 flags EF Core async calls that omit an available CancellationToken, so cancelled requests keep running queries against the database."
---

# LC026: Missing CancellationToken in Async Call

## In Plain Terms

Imagine you ask a robot to go get you a ball from a very far away field.
Halfway there, you change your mind and shout "Stop!" If the robot isn't listening for your shout (no CancellationToken),
it will walk all the way to the field, get the ball, and walk all the way back, even though you don't want it anymore.
It’s a waste of the robot's battery!

## What It Flags

LC026 reports EF Core async calls that can accept a `CancellationToken` but omit it or pass `default` while a usable token is available at the call site.

```csharp
public async Task<List<User>> GetUsers(CancellationToken ct)
{
    return await db.Users.ToListAsync(); // LC026
}
```

```csharp
public async Task Save(CancellationToken cancellationToken)
{
    await db.SaveChangesAsync(default); // LC026
}
```

## Why It Matters

Database work can continue long after the request, worker, or background operation that started it has been cancelled. Passing the available token lets EF Core and the provider stop query execution, release connections sooner, and avoid doing work no caller still needs.

The rule is informational because cancellation plumbing is sometimes policy-driven. It is still worth keeping visible on request paths, hosted services, and any expensive query or save operation.

## Safer Shape

Pass the token that represents the current operation.

```csharp
public async Task<List<User>> GetUsers(CancellationToken ct)
{
    return await db.Users.ToListAsync(ct);
}
```

For named optional arguments, keep the argument name and replace only the ignored token value.

```csharp
await db.Users.ToListAsync(cancellationToken: cancellationToken);
```

## Token Selection

LC026 only reports when a token is available in local scope. The fixer uses this selection order:

1. A token named `cancellationToken`.
2. A token named `ct`.
3. The first available token discovered by the compiler at the invocation location.

Eligible tokens can be:

- method or lambda parameters
- locals
- fields
- readable properties

Fields and properties are inserted by bare name, which binds correctly to instance members from an instance method.

```csharp
private CancellationToken RequestAborted { get; }

public async Task<List<User>> Load(DbSet<User> users)
{
    return await users.ToListAsync(RequestAborted);
}
```

When several domain-specific tokens are in scope, the rule deliberately does not infer business intent beyond the simple naming preference above. Rename the intended token to `cancellationToken` or `ct`, pass it manually, or suppress the diagnostic if a different token boundary is intentional.

## What Counts as Missing

These shapes report when a usable token is in scope:

```csharp
await db.Users.ToListAsync();
await db.Users.ToListAsync(default);
await db.Users.ToListAsync(cancellationToken: default);
await db.SaveChangesAsync();
```

These shapes stay quiet:

```csharp
await db.Users.ToListAsync(cancellationToken);
await db.Users.ToListAsync(ct);
await db.Users.ToListAsync(); // no CancellationToken is available in scope
await db.AuditLog.AddAsync(entry, CancellationToken.None); // deliberately not cancellable
```

`CancellationToken.None` is the explicit way to say an operation must finish even when the caller gives up, for example an audit write or a compensating save in `finally` or `catch (OperationCanceledException)`. LC026 treats it as intentional. To report it anyway, set:

```ini
dotnet_code_quality.LC026.report_explicit_none = true
```

A token only counts as usable when the call could actually pass it. A local declared later in the method (CS0841) and an instance field or property seen from a `static` method (CS0120) do not count, so neither the diagnostic nor the fix relies on them.

## Boundaries

LC026 is a local rule. It does not trace tokens through service abstractions, infer whether a field represents the current request, or decide between multiple domain-specific tokens such as `shutdownToken` and `requestAborted`.

It also only targets EF Core async methods that expose a `CancellationToken` parameter. Non-EF async APIs are outside this rule's scope.

## Fix Strategy

The code fix appends the selected token when the token argument is omitted. When the call already supplies `default`, `cancellationToken: default`, or (with `report_explicit_none` on) `CancellationToken.None`, the fixer replaces that argument instead of appending a duplicate.

For query chains, the fixer updates the EF async terminal that owns the diagnostic rather than inner LINQ operators:

```csharp
await db.Users.Where(user => user.Active).ToListAsync(cancellationToken);
```

No rewrite is offered when no usable token exists, because creating a fresh token source would not connect the database operation to the caller's cancellation boundary.
