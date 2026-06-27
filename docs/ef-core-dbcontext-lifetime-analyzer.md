---
layout: default
title: EF Core DbContext Lifetime Analyzer
description: Use LinqContraband as an EF Core DbContext lifetime analyzer for singleton DbContexts, cross-thread captures, disposed query leaks, mixed tracking modes, and silent no-tracking writes.
permalink: /ef-core-dbcontext-lifetime-analyzer/
body_class: page-dbcontext-lifetime-analyzer
---

# EF Core DbContext Lifetime Analyzer

LinqContraband includes EF Core DbContext lifetime analyzers for code that keeps a context alive too long, captures it
across threads, returns deferred queries after disposal, or mixes tracking behaviour in ways that make saves unreliable.

Install the official analyzer package:

```bash
dotnet add package LinqContraband
```

## Why DbContext Lifetime Matters

`DbContext` is designed to be a short-lived unit-of-work object. It is not thread-safe, it owns a change tracker, and it
usually belongs to a request, job step, or explicit scope.

This shape is risky because the hosted service is long-lived while the context is scoped:

```csharp
public sealed class Worker : BackgroundService
{
    private readonly AppDbContext _db;

    public Worker(AppDbContext db) => _db = db;
}
```

Prefer a factory or an explicit scope inside the work item:

```csharp
public sealed class Worker : BackgroundService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public Worker(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var db = await _factory.CreateDbContextAsync(stoppingToken);
        // Query or save within this operation's lifetime.
    }
}
```

## What The Analyzer Flags

| Rule | Finds | Why it matters |
| --- | --- | --- |
| [LC030: DbContext lifetime mismatch](/LinqContraband/LC030_DbContextInSingleton.html) | DbContexts stored in hosted services, singleton services, middleware-like long-lived types, or singleton DI registrations. | A context can leak tracked state, cross request boundaries, or be used concurrently. |
| [LC036: DbContext captured across threads](/LinqContraband/LC036_DbContextCapturedAcrossThreads.html) | DbContext locals, fields, properties, or parameters captured into `Task.Run`, `Parallel.ForEach`, `Thread`, timer, or thread-pool callbacks. | The same context can be used after its owning scope or concurrently on another thread. |
| [LC013: disposed context query leak](/LinqContraband/LC013_DisposedContextQuery.html) | Deferred `IQueryable` or `IAsyncEnumerable` values returned after the context that built them is disposed. | Callers get an `ObjectDisposedException` when enumeration finally happens. |
| [LC044: no-tracking entity mutated then saved](/LinqContraband/LC044_AsNoTrackingThenModifySilentWrite.html) | `AsNoTracking` entities mutated before `SaveChanges` without re-attaching. | EF Core can silently persist nothing. |
| [LC040: mixed tracking modes](/LinqContraband/LC040_MixedTrackingAndNoTracking.html) | Tracked and no-tracking materialization from the same context in one scope. | Later update behaviour can become unclear or inconsistent. |

## Cross-Thread Capture Example

This captures a scoped context into work that can run later or concurrently:

```csharp
Task.Run(() => db.Users.Count());
Parallel.ForEach(ids, id => db.Users.Find(id));
```

Create a new context inside the background delegate instead:

```csharp
await Task.Run(async () =>
{
    await using var db = await factory.CreateDbContextAsync();
    return await db.Users.CountAsync();
});
```

## Disposed Query Leak Example

Deferred execution can outlive the context that created the query:

```csharp
public IQueryable<User> ActiveUsers()
{
    using var db = new AppDbContext();
    return db.Users.Where(user => user.IsActive);
}
```

Materialize before disposal, or let dependency injection own the context lifetime:

```csharp
public async Task<List<User>> ActiveUsersAsync()
{
    await using var db = await factory.CreateDbContextAsync();
    return await db.Users
        .Where(user => user.IsActive)
        .ToListAsync();
}
```

## Safer Lifetime Patterns

- Keep a `DbContext` scoped to one request, job step, command handler, or explicit unit of work.
- Inject `IDbContextFactory<TContext>` into hosted services, timers, queue processors, and parallel workers.
- Create a new context or scope inside background delegates.
- Materialize deferred queries before a locally created context is disposed.
- Keep read-only no-tracking flows separate from write flows, or make the tracking boundary explicit.
- Use explicit re-attach calls only when a reviewer accepts the wider update semantics.

## Review Checklist

1. Is any `DbContext` stored on a singleton, hosted service, middleware-like type, timer, or background worker?
2. Does any `Task.Run`, `Parallel.ForEach`, `Thread`, timer, or thread-pool callback capture a context from outside?
3. Does any method return `IQueryable` or `IAsyncEnumerable` from a `using` or locally disposed context?
4. Are `AsNoTracking` entities later mutated or passed back into EF write APIs?
5. Would separate scopes, `IDbContextFactory<TContext>`, or a scoped collaborator make the lifetime clearer?

## CI Severity Starter

Treat context lifetime and cross-thread misuse as early warnings. Teams with production incidents around scoped services
often promote LC036 to an error once existing findings are cleared:

```ini
[*.cs]

# DbContext lifetime and tracking reliability
dotnet_diagnostic.LC030.severity = warning
dotnet_diagnostic.LC036.severity = warning
dotnet_diagnostic.LC013.severity = warning
dotnet_diagnostic.LC044.severity = warning
dotnet_diagnostic.LC040.severity = suggestion
```

Use the [EF Core query analyzer CI guide](/LinqContraband/ef-core-query-analyzer-ci/) when these diagnostics should run
on every pull request. Use the [EF Core AsNoTracking analyzer guide](/LinqContraband/ef-core-asnotracking-analyzer/)
when the main concern is read-only tracking policy rather than service lifetime.

## Official Links

- Canonical repository: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Official NuGet package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Full rule catalog: [georgepwall1991.github.io/LinqContraband/rule-catalog.html](https://georgepwall1991.github.io/LinqContraband/rule-catalog.html)
- Safe install guidance: [Official LinqContraband downloads and authenticity](/LinqContraband/security-and-authenticity/)
