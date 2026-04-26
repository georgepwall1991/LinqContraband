# LC023 - Use Find/FindAsync for Primary Key Lookups

## Goal

Suggest `Find(...)` or `FindAsync(...)` when a query directly looks up a `DbSet` entity by its primary key with `FirstOrDefault`, `SingleOrDefault`, `FirstOrDefaultAsync`, or `SingleOrDefaultAsync`.

## Why It Matters

`FirstOrDefault` and `SingleOrDefault` always execute a query. `Find` first checks the EF Core change tracker and can return an already-loaded entity without another database round trip.

## Violation

```csharp
var user = db.Users.FirstOrDefault(user => user.Id == userId);
```

## Preferred

```csharp
var user = db.Users.Find(userId);
```

For awaited async lookups, the fixer preserves explicit cancellation tokens by using EF Core's token-aware overload:

```csharp
var user = await db.Users.FindAsync(new object[] { userId }, cancellationToken);
```

## Analyzer Behavior

`LC023` reports only when all of these are true:

- The method is `FirstOrDefault`, `SingleOrDefault`, `FirstOrDefaultAsync`, or `SingleOrDefaultAsync`.
- The receiver is directly a `DbSet<TEntity>`.
- The predicate is a simple equality check against the entity primary key convention or `[Key]` property.

The analyzer intentionally stays silent for chained queries such as `AsNoTracking()`, `Include(...)`, `Where(...)`, and other shapes where replacing the materializer with `Find` would change tracking, includes, filters, or return semantics.

## Fixer Behavior

The code fix rewrites simple synchronous lookups to `Find(key)` and awaited async lookups to `FindAsync(key)`.

When the awaited async lookup passes an explicit cancellation token, the fixer rewrites to `FindAsync(new object[] { key }, cancellationToken)` so the token is preserved. The fixer does not rewrite non-awaited async calls because EF Core `FindAsync` returns `ValueTask<TEntity?>`, which can be incompatible with a call site expecting `Task<TEntity?>`.

## Non-Goals

`LC023` does not infer composite keys, Fluent API key configuration, or provider-specific behavior beyond the repository's primary-key helper. Those shapes should be handled manually unless future analyzer metadata can prove the rewrite is safe.
