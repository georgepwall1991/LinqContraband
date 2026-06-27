---
layout: default
title: EF Core CancellationToken Analyzer
description: Use LinqContraband as an EF Core missing CancellationToken analyzer for ToListAsync, FirstOrDefaultAsync, SaveChangesAsync, and other async database calls.
permalink: /ef-core-cancellation-token-analyzer/
body_class: page-cancellation-token-analyzer
---

# EF Core CancellationToken Analyzer

LinqContraband is an EF Core `CancellationToken` analyzer for .NET teams that want async database calls to respect
request cancellation and worker shutdown. It flags `ToListAsync`, `FirstOrDefaultAsync`, `SaveChangesAsync`, and other
EF Core async calls when a usable token is in scope but the call ignores it.

Install the official analyzer package:

```bash
dotnet add package LinqContraband
```

## Why Missing CancellationToken Matters

Async EF Core code can still waste database and connection-pool resources after the caller has gone away. A cancelled
HTTP request, abandoned background job, or stopping hosted service should not leave expensive query work running just
because the token was dropped at the repository boundary.

The missing-token shape looks like this:

```csharp
public async Task<List<User>> ActiveUsers(CancellationToken cancellationToken)
{
    return await db.Users
        .Where(user => user.IsActive)
        .ToListAsync(); // LC026
}
```

The safer shape passes the operation token to the EF Core async API:

```csharp
public async Task<List<User>> ActiveUsers(CancellationToken cancellationToken)
{
    return await db.Users
        .Where(user => user.IsActive)
        .ToListAsync(cancellationToken);
}
```

If the broader issue is a synchronous EF Core API inside an async method, use the
[EF Core async query analyzer guide](/LinqContraband/ef-core-async-query-analyzer/) as the starting point.

## Rule Covered By This Guide

| Rule | What it detects | Review direction |
| --- | --- | --- |
| [LC026: missing CancellationToken](/LinqContraband/LC026_MissingCancellationToken.html) | EF Core async calls that omit a token, pass `default`, or pass `CancellationToken.None` while a usable token is in local scope. | Pass the operation token through the query, save, find, and bulk-write API unless intentionally decoupling the database work from caller cancellation. |

## Common EF Core CancellationToken Problems

### ToListAsync without the request token

```csharp
public async Task<IReadOnlyList<Order>> RecentOrders(DateTime cutoff, CancellationToken cancellationToken)
{
    return await db.Orders
        .Where(order => order.CreatedAt >= cutoff)
        .ToListAsync(); // LC026
}
```

```csharp
public async Task<IReadOnlyList<Order>> RecentOrders(DateTime cutoff, CancellationToken cancellationToken)
{
    return await db.Orders
        .Where(order => order.CreatedAt >= cutoff)
        .ToListAsync(cancellationToken);
}
```

### Predicate overloads missing the final token argument

```csharp
public async Task<User?> FindUser(int id, CancellationToken ct)
{
    return await db.Users.FirstOrDefaultAsync(user => user.Id == id); // LC026
}
```

```csharp
public async Task<User?> FindUser(int id, CancellationToken ct)
{
    return await db.Users.FirstOrDefaultAsync(user => user.Id == id, ct);
}
```

### SaveChangesAsync ignoring cancellation

```csharp
public async Task RenameUser(User user, string name, CancellationToken cancellationToken)
{
    user.Name = name;
    await db.SaveChangesAsync(); // LC026
}
```

```csharp
public async Task RenameUser(User user, string name, CancellationToken cancellationToken)
{
    user.Name = name;
    await db.SaveChangesAsync(cancellationToken);
}
```

### Explicit ignored token values

```csharp
await db.Users.ToListAsync(default); // LC026
await db.Users.ToListAsync(CancellationToken.None); // LC026
await db.Users.ToListAsync(cancellationToken: default); // LC026
```

```csharp
await db.Users.ToListAsync(cancellationToken);
await db.Users.ToListAsync(cancellationToken: cancellationToken);
```

## Token Selection

LC026 only reports when a usable `CancellationToken` is already available at the invocation site. The code fix chooses:

1. A token named `cancellationToken`.
2. A token named `ct`.
3. The first available token the compiler exposes at that location.

Eligible tokens can come from method parameters, lambda parameters, locals, fields, or readable properties.

```csharp
private CancellationToken RequestAborted { get; }

public async Task<List<User>> LoadUsers()
{
    return await db.Users.ToListAsync(RequestAborted);
}
```

When several domain-specific tokens are in scope, LinqContraband keeps the rule local and conservative. Rename the
intended token to `cancellationToken` or `ct`, pass the chosen token manually, or suppress the diagnostic when the query
should deliberately outlive the caller.

## Boundaries

LC026 does not create a new token source and does not report when no token is available in scope. A new
`CancellationTokenSource` at the call site would not connect the database operation to the caller's cancellation
boundary.

The rule is intentionally limited to EF Core async APIs with a `CancellationToken` parameter. It does not trace tokens
through service abstractions or decide whether a field such as `shutdownToken`, `requestAborted`, or `jobToken`
represents the right business boundary.

## CI Severity Starter

Start as a suggestion while existing async paths are cleaned up, then promote to warning when request and worker
handlers are expected to pass tokens consistently:

```ini
[*.cs]

# Async EF Core cancellation
dotnet_diagnostic.LC026.severity = suggestion

# Related async execution rules
dotnet_diagnostic.LC008.severity = warning
dotnet_diagnostic.LC043.severity = suggestion
```

Use the [EF Core query analyzer CI guide](/LinqContraband/ef-core-query-analyzer-ci/) when missing-token diagnostics
should show up on every pull request.

## Related Guides

- [EF Core async query analyzer](/LinqContraband/ef-core-async-query-analyzer/)
- [EF Core query performance checklist](/LinqContraband/ef-core-query-performance-checklist/)
- [EF Core analyzer rules](/LinqContraband/ef-core-analyzer-rules/)
- [EF Core DbContext lifetime analyzer](/LinqContraband/ef-core-dbcontext-lifetime-analyzer/)
- [EF Core query analyzer for CI](/LinqContraband/ef-core-query-analyzer-ci/)

## Official Links

- Canonical repository: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Official NuGet package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Full rule catalog: [georgepwall1991.github.io/LinqContraband/rule-catalog.html](https://georgepwall1991.github.io/LinqContraband/rule-catalog.html)
- Safe install guidance: [Official LinqContraband downloads and authenticity](/LinqContraband/security-and-authenticity/)
