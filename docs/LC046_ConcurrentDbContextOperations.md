---
layout: default
title: "Spec: LC046 - Concurrent DbContext Operations"
---

# Spec: LC046 - Concurrent DbContext Operations

## Goal

Detect overlapping Entity Framework Core operations that are proven to use the same `DbContext` instance.

## The Problem

Entity Framework Core does not support multiple parallel operations on one context. Starting another query or save
before the previous task completes can throw `InvalidOperationException`; when the overlap escapes EF Core's guard,
the context's state is undefined.

### Example Violation

```csharp
var users = db.Users.ToListAsync(cancellationToken);
var roles = db.Roles.ToListAsync(cancellationToken); // LC046
await Task.WhenAll(users, roles);
```

The same risk appears when one operation is a query and the other is a save, bulk command, `FindAsync`, `LoadAsync`,
or relational raw command.

## Safer Shapes

Await operations sequentially when they belong to one unit of work:

```csharp
var users = await db.Users.ToListAsync(cancellationToken);
var roles = await db.Roles.ToListAsync(cancellationToken);
```

When the work is genuinely independent and parallelism is intentional, create a separate context for each operation:

```csharp
var usersTask = LoadUsersAsync(factory, cancellationToken);
var rolesTask = LoadRolesAsync(factory, cancellationToken);
await Task.WhenAll(usersTask, rolesTask);
```

Each helper must create and dispose its own context.

## Analyzer Logic

### ID: `LC046`
### Category: `Safety`
### Severity: `Warning`

LC046 reports the second proven overlapping EF Core invocation and points back to the first operation as an additional
location. It recognises async query materializers and aggregates, `FindAsync`, `SaveChangesAsync`, `LoadAsync`,
`ExecuteUpdateAsync`, `ExecuteDeleteAsync`, and relational `ExecuteSql*Async` commands.

The analyzer follows stable locals, parameters, readonly fields, source-visible auto-properties, `DbSet` members,
`DbContext.Set<TEntity>()`, and transparent LINQ or EF query chains. It also reports
`Task.WhenAll(items.Select(...))` when the selector captures one outer context and the source can contain multiple
items. Instance context members are matched by both the member and its proven receiver, so the same member on two
different holder objects is not conflated.

To preserve precision, LC046 stays quiet for sequential awaits, separate contexts, branch-exclusive operations,
reassigned or escaped task/context state, repository-produced `IQueryable` values, computed context or set properties,
custom lookalike APIs, query construction, `AsAsyncEnumerable()` alone, and per-item context factories. LC036 continues
to own `Task.Run`, `Parallel`, `Thread`, thread-pool, and timer capture diagnostics.

An await or task escape suppresses the diagnostic only when it is guaranteed to execute before the later EF Core
operation. A conditional await or an exception path that can bypass an await still reports because another reaching
path can leave the first operation active. Each independently drained and restarted overlap group receives its own
diagnostic. Selector analysis inspects only code executed by the selector itself, not uninvoked nested lambdas or
local functions.

## Why There Is No Code Fix

Sequential execution and separate contexts change performance, lifetime, transaction, tracking, and consistency
semantics. Choosing between them requires application intent, so LC046 is diagnostic-only.
