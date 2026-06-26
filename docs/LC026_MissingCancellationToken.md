---
layout: default
title: "LC026: Missing CancellationToken in Async Call"
---

# LC026: Missing CancellationToken in Async Call

## What It Flags

LC026 reports EF Core async calls that can accept a `CancellationToken` but omit it, pass `default`, or pass `CancellationToken.None` while a usable token is available at the call site.

```csharp
public async Task<List<User>> GetUsers(CancellationToken ct)
{
    return await db.Users.ToListAsync(); // LC026
}
```

```csharp
public async Task Save(CancellationToken cancellationToken)
{
    await db.SaveChangesAsync(CancellationToken.None); // LC026
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
await db.Users.ToListAsync(CancellationToken.None);
await db.SaveChangesAsync();
```

These shapes stay quiet:

```csharp
await db.Users.ToListAsync(cancellationToken);
await db.Users.ToListAsync(ct);
await db.Users.ToListAsync(); // no CancellationToken is available in scope
```

## Boundaries

LC026 is a local rule. It does not trace tokens through service abstractions, infer whether a field represents the current request, or decide between multiple domain-specific tokens such as `shutdownToken` and `requestAborted`.

It also only targets EF Core async methods that expose a `CancellationToken` parameter. Non-EF async APIs are outside this rule's scope.

## Fix Strategy

The code fix appends the selected token when the token argument is omitted. When the call already supplies `default`, `cancellationToken: default`, or `CancellationToken.None`, the fixer replaces that argument instead of appending a duplicate.

No rewrite is offered when no usable token exists, because creating a fresh token source would not connect the database operation to the caller's cancellation boundary.
