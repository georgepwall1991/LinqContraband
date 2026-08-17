---
layout: default
title: "Spec: LC047 - ExecuteDelete bypasses the tracked delete pipeline"
---

# Spec: LC047 - ExecuteDelete bypasses the tracked delete pipeline

## Goal

Detect `ExecuteDelete` / `ExecuteDeleteAsync` when the owning `DbContext` has a proven tracked delete pipeline that SQL `DELETE` will skip: a `SaveChanges` / interceptor conversion of `EntityState.Deleted`, or a Fluent `OnDelete(ClientCascade|ClientSetNull)` relationship whose principal is the deleted entity.

## The Problem

`ExecuteDelete` issues a SQL `DELETE`. It does not run `SaveChanges` overrides, `ISaveChangesInterceptor` / `SavingChanges`, or client-cascade fix-up. Global query filters still apply as a `WHERE`, so a `HasQueryFilter(e => !e.IsDeleted)` model looks protected while live rows are physically destroyed.

That is the same silent-data-loss class as LC044, and it is worse in one package-specific way: LC012 rewrites `RemoveRange(query)` to `ExecuteDelete()`. On a soft-delete or client-cascade model that conversion is unsafe. LC012 therefore stays quiet when LC047 evidence covers the same entity and context.

```csharp
public override int SaveChanges()
{
    foreach (var entry in ChangeTracker.Entries())
    {
        if (entry.State == EntityState.Deleted)
        {
            entry.State = EntityState.Modified;
            ((ISoftDelete)entry.Entity).IsDeleted = true;
        }
    }
    return base.SaveChanges();
}

// Tracked path: soft-delete. ExecuteDelete physically deletes live rows.
await db.Users.Where(u => u.LastLogin < cutoff).ExecuteDeleteAsync();
```

## Safer shapes

When the conversion writes a single bool property such as `IsDeleted = true`, keep the set-based write and use `ExecuteUpdate`:

```csharp
await db.Users
    .Where(u => u.LastLogin < cutoff)
    .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.IsDeleted, true));
```

When the pipeline is a client cascade, multi-property conversion, or helper-method interceptor, keep the tracked unit of work:

```csharp
db.Orders.RemoveRange(db.Orders.Where(o => o.Id < cutoff));
await db.SaveChangesAsync();
```

## Analyzer Logic

### ID: `LC047`
### Category: `Safety`
### Severity: `Warning`

LC047 reports only when pipeline evidence is proven in the current compilation.

**Proof A — SaveChanges delete conversion.** A `DbContext` subclass reachable from the query's context type, or a `SaveChangesInterceptor` / `ISaveChangesInterceptor` registered from that type's `OnConfiguring` (`AddInterceptors`) or from a compilation-visible `AddDbContext<TContext>` / `AddDbContextPool<TContext>` / `AddDbContextFactory<TContext>` call on `Microsoft.Extensions.DependencyInjection.EntityFrameworkServiceCollectionExtensions` whose generic argument is that source context and whose inline options lambda calls `DbContextOptionsBuilder.AddInterceptors`, has a `SaveChanges` / `SaveChangesAsync` / `SavingChanges` / `SavingChangesAsync` body that:

- reads `EntityState.Deleted` in a dominating state test, and
- under that dominance, either assigns `EntityState.Modified` / `Unchanged` or writes a property / `Property("…").CurrentValue` on that entry.

Dominance is proven when the conversion sits in the same `if` body as `entry.State == EntityState.Deleted` or `entry.State is EntityState.Deleted`, after `if (entry.State != EntityState.Deleted) continue` / `return` in the same block, or in a `switch` arm whose labels include `EntityState.Deleted`. Same-type helpers and local functions inherit dominance from the call site and are followed to depth 4. Uncalled local functions and anonymous functions are not scanned. Untyped `Entries()` with no cast covers the whole context. `Entries<T>()` or a cast of `.Entity` to `T` / an interface covers that type and proven derived types or implementers. Constructed generic contexts such as `AppDbContext<int>` match conversion stored on `AppDbContext<TTenant>`. A registered interceptor may be a derived type, or a base-typed local assigned from `new TInterceptor()` inside or before the options lambda. DI registration is proven from constructors, ordinary methods, and top-level statements. Method-group helpers, lambdas that only call another helper, non-generic `AddDbContext(Type, ...)`, lookalike extension methods (including a same-named type in `Microsoft.EntityFrameworkCore`), and interceptors with no source body stay quiet. Sibling Deleted-log plus Added/Modified writes, conversion on the `else` of a Deleted check, `EntityState.Detached` assignment, and unproven exception/loop/goto shapes stay quiet rather than guessing.

**Proof B — client cascade.** Fluent `OnDelete(DeleteBehavior.ClientCascade)` or `ClientSetNull` on a relationship whose principal is the `ExecuteDelete` entity. `OnDelete` on `ReferenceCollectionBuilder<TPrincipal, TDependent>` uses `TPrincipal`. One-to-one `HasOne().WithOne()` reports only when `HasForeignKey<TDependent>` names the dependent; without that, both sides stay quiet rather than guessing. Same-type helpers called from `OnModelCreating` are followed to depth 4. Applied `IEntityTypeConfiguration<T>` is followed when `OnModelCreating` (or a same-type helper) calls `ApplyConfiguration` with a proven configuration instance (`new TConfig()`, a single-assignment local, or a derived type), or `ApplyConfigurationsFromAssembly` with a proven current-compilation assembly (`typeof(T).Assembly`, `Assembly.GetExecutingAssembly()`, or a single-assignment local of either) and no predicate argument. FromAssembly only scans non-abstract, non-generic classes with a public parameterless constructor, matching EF's constructible-type filter (including nested types of open generics). `Configure` is resolved through the interface implementation actually invoked (`FindImplementationForInterfaceMember`), so a derived override that replaces `ClientCascade` with database `Cascade` stays quiet. Same-type helpers on that implementing type, including constructed generic configuration types, are scanned with the same `OnDelete` principal rules. Database `Cascade` stays quiet because SQL will cascade. `ExecuteDelete` on the dependent stays quiet. Unapplied configuration classes are ignored.

### When it stays quiet (non-goals)

- `HasQueryFilter(e => !e.IsDeleted)` alone. Filter-only models still hard-delete on `Remove` + `SaveChanges`.
- Name heuristics (`IsDeleted`, `DeletedAt`) without a proven Deleted-state handler.
- `ExecuteUpdate` skipping `UpdatedAt` / interceptors (different intent).
- Unregistered interceptors, interceptors registered only through an opaque DI helper (method group or lambda that only forwards to another method), interceptors registered against a different generic context, and interceptors with no source in the compilation.
- `ApplyConfigurationsFromAssembly` of an external or otherwise unproven assembly, a call that passes a `Func<Type, bool>` predicate, or a current-assembly scan of abstract, generic (including nested types of open generics), or non-public-constructor configuration types. Proof B does not scan every `IEntityTypeConfiguration<T>` in the compilation unless that current-assembly application is proven and EF would construct the type.
- `HasOne().WithOne()` without `HasForeignKey<TDependent>`.
- Lookalike `ExecuteDelete` helpers outside `Microsoft.EntityFrameworkCore`.
- `ExecuteDelete` on a framework `DbContext` parameter whose concrete type is unknown.
- A different context type than the one that owns the proven pipeline.
- Path-insensitive Deleted reads paired with conversion on a sibling `if` / `switch` arm, after `if (Deleted) continue`, in an uncalled local function, or in an unproven exception/loop/goto shape. Full CFG modeling is a non-goal of this proof.

## Code Fix

When Proof A names a **single** property assigned constant `true` and that property exists on the entity, the fixer rewrites:

| Call | Rewrite |
| --- | --- |
| `query.ExecuteDelete()` | `query.ExecuteUpdate(setters => setters.SetProperty(e => e.Prop, true))` |
| `query.ExecuteDeleteAsync(...)` | `query.ExecuteUpdateAsync(setters => setters.SetProperty(e => e.Prop, true), ...)` |
| `RelationalQueryableExtensions.ExecuteDelete(query)` | `RelationalQueryableExtensions.ExecuteUpdate(query, setters => setters.SetProperty(e => e.Prop, true))` |

Client cascade, helper-method interceptors, shadow-only properties, and multi-property conversions stay diagnostic-only: the safe alternative is `RemoveRange` + `SaveChanges` or an explicit child delete, which changes unit-of-work timing.

## LC012 coupling

If Proof A or Proof B covers the `RemoveRange` entity and context, LC012 does not report and does not offer `ExecuteDelete`. When the `RemoveRange` receiver does not resolve to a context (a `DbSet<T>` parameter), LC012 also stays quiet if any source `DbContext` in the compilation covers that entity. Shipping LC047 without that gate would diagnose a hard delete and auto-fix into it.
