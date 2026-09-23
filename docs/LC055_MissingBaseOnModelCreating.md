---
layout: default
title: "LC055: OnModelCreating override skips the base configuration"
description: "LC055 flags EF Core OnModelCreating overrides that never call base.OnModelCreating when the base context, such as IdentityDbContext, configures the model."
---

# LC055: OnModelCreating override skips the base configuration

## In Plain Terms

You inherited a house with the wiring already done, then rebuilt the kitchen from the floor plan without looking at the wiring diagram. The lights you never touched stop working.

## Goal

Detect `OnModelCreating` overrides in a `DbContext` whose base context configures the model, when nothing in the context ever calls `base.OnModelCreating(...)`.

## The Problem

Contexts such as ASP.NET Core Identity's `IdentityDbContext` set up keys, indexes, and relationships for their own entities in `OnModelCreating`. Shared base contexts in your own code often add global query filters, value conversions, or owned types there. An override that forgets `base.OnModelCreating(builder)` throws all of that away.

With Identity the app usually fails at startup with "The entity type 'IdentityUserLogin&lt;string&gt;' requires a primary key to be defined". With a project base context the model is silently wrong: a soft-delete or tenant filter is missing, and queries return rows they should hide.

```csharp
public class AppDbContext : IdentityDbContext<AppUser>
{
    // Violation: Identity's model configuration never runs.
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Order>().HasKey(o => o.Id);
    }
}
```

## The Fix

Call the base implementation first, then add or refine your own configuration:

```csharp
protected override void OnModelCreating(ModelBuilder builder)
{
    base.OnModelCreating(builder);
    builder.Entity<Order>().HasKey(o => o.Id);
}
```

## Analyzer Logic

### ID: `LC055`
### Category: `Correctness`
### Severity: `Warning`

1. Find an `OnModelCreating(ModelBuilder)` override with a body in a type that derives from `DbContext`.
2. Look at the method it overrides. Stay quiet when that is EF Core's own `DbContext.OnModelCreating`, which is empty, or a source override whose body is empty or only forwards to its own empty base. A base from another assembly (Identity, a shared package) is assumed to configure the model.
3. Report on the method name when no `base.OnModelCreating(...)` call appears anywhere in the context, including other methods, lambdas, and other `partial` parts.

## When it stays quiet (non-goals)

- Contexts that derive from `DbContext` directly.
- Base overrides in source that are empty or only call `base.OnModelCreating(...)` down to `DbContext`.
- A base `OnModelCreating` declared `abstract`, which cannot be called.
- Any `base.OnModelCreating(...)` call in the type, even conditional or in a helper. The rule does not check that every path calls it.
- Types that are not `DbContext`s, even when they have an `OnModelCreating(ModelBuilder)` method.

A compiled base context whose `OnModelCreating` happens to be empty is still reported, because its body cannot be inspected. Adding the base call is harmless there.

## Code Fix

Inserts `base.OnModelCreating(<parameter>);` as the first statement, so your configuration still runs after the base configuration and can override it. An expression-bodied override (`=> builder.Entity<Order>()...`) becomes a block with the base call followed by the original expression.

## Test Cases

### Violations

```csharp
public class AppDbContext : IdentityDbContext
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Order>().HasKey(o => o.Id);
    }
}
```

### Valid

```csharp
public class AppDbContext : IdentityDbContext
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Order>().HasKey(o => o.Id);
    }
}

public class ShopContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Order>().HasKey(o => o.Id);
    }
}
```
