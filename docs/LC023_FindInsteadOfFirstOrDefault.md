---
layout: default
title: "LC023: Use Find/FindAsync for Primary Key Lookups"
description: "LC023 suggests Find or FindAsync when FirstOrDefault or SingleOrDefault looks up an EF Core entity by primary key, so tracked entities skip the database."
---

# LC023: Use Find/FindAsync for Primary Key Lookups

## In Plain Terms

Imagine you want to know if you have a blue shirt. `FirstOrDefault` is like
driving to the store to buy a new one just to check. `Find` is like looking in your closet first. If it's in the closet,
you save a trip!

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
- The predicate is a simple equality check against the proven entity primary key.

The analyzer intentionally stays silent for chained queries such as `AsNoTracking()`, `Include(...)`, `Where(...)`, and other shapes where replacing the materializer with `Find` would change tracking, includes, filters, or return semantics.

When visible EF Fluent API configuration declares `EntityTypeBuilder<TEntity>.HasKey(...)`, that configuration takes precedence over convention-based `Id`/`{EntityName}Id` inference. LC023 reports for a single configured key property, stays silent when the predicate targets a different convention property, and ignores unrelated generic helper methods named `HasKey` so non-EF metadata cannot override the entity key. Attribute-based fallback requires the real `System.ComponentModel.DataAnnotations.KeyAttribute`; project-local same-name attributes are ignored. The analyzer also stays silent for composite or otherwise unsupported key expressions because `Find(...)` would require the complete key value set in the configured order.

EF Core's class-level key attributes are honored too: `[Keyless]` and `HasNoKey()` entities stay silent (`Find` throws on a keyless entity type), `[PrimaryKey(nameof(ExternalId))]` makes `ExternalId` the key instead of `Id`, and a composite `[PrimaryKey(a, b)]` stays silent. Fluent configuration wins over the attributes, which win over `[Key]` and the `Id`/`{EntityName}Id` convention, matching EF's own precedence.

### Configuration is read from the whole compilation

`HasKey`, `HasNoKey` and `HasQueryFilter` calls are found wherever they live in the project (the `DbContext`, `IEntityTypeConfiguration<T>` classes, or any other file), however many files the project has. The scan runs once per compilation and only binds files whose text mentions one of those three names, so its cost follows the size of the model configuration rather than the size of the project. Up to 5.10.0 the analyzer skipped this scan and the convention-key fallback in projects with more than 64 syntax trees, which silenced it on `x.Id == id` in most real applications.

### Global query filters gate the rule

`Find` checks the change tracker **before** querying, and a tracker hit bypasses global query filters — only the database fallback applies them (`EntityFinder` falls through to `queryRoot.FirstOrDefault(...)`). On an entity configured with `HasQueryFilter(...)` (soft delete, multi-tenancy), rewriting `FirstOrDefault(x => x.Id == id)` to `Find(id)` can therefore return an already-tracked row the filtered query would have excluded — a silent correctness change, not a perf win. LC023 stays completely silent for entities with a visible `HasQueryFilter(...)` — in `OnModelCreating`, an `EntityTypeBuilder<TEntity>` configuration class, or the non-generic `modelBuilder.Entity(typeof(X)).HasQueryFilter(...)` form. The check uses the `DbSet`'s entity type (a key inherited from a base class doesn't dodge the gate) and walks base types, since EF declares filters on the hierarchy root and propagates them to derived entities. A lookalike `HasQueryFilter` on a non-EF builder type does not suppress. A filter configured in another assembly is invisible to the analyzer — treat `Find` on filtered entities as a manual review point.

## Fixer Behavior

The code fix rewrites simple synchronous lookups to `Find(key)` and awaited async lookups to `FindAsync(key)`.

When the awaited async lookup passes an explicit cancellation token, the fixer rewrites to `FindAsync(new object[] { key }, cancellationToken)` so the token is preserved. The fixer does not rewrite non-awaited async calls because EF Core `FindAsync` returns `ValueTask<TEntity?>`, which can be incompatible with a call site expecting `Task<TEntity?>`.

The fixer also requires the key value to be independent of the predicate parameter. LC023 may still report a column-to-column predicate such as `users.FirstOrDefault(x => x.Id == x.OtherId)`, but it stays manual because rewriting to `users.Find(x.OtherId)` would reference `x` outside the lambda and break the build.

## Non-Goals

`LC023` does not rewrite partial composite-key lookups, non-lambda Fluent API key expressions, column-to-column key predicates, fake key attributes, or provider-specific behavior beyond visible `HasKey(...)`/`HasNoKey()`, EF Core's `[Keyless]`/`[PrimaryKey]`, real `[Key]`, and convention metadata. Configuration in another assembly, or built through a helper that calls `HasKey` indirectly, is not visible. Those shapes should be handled manually unless future analyzer metadata can prove the rewrite is safe.
