---
layout: default
title: "LC027: Missing Explicit Foreign Key Property"
description: "LC027 flags EF Core reference navigations whose dependent entity has no matching foreign key property, leaving EF Core to create a shadow key."
---

# LC027: Missing Explicit Foreign Key Property

## In Plain Terms

Imagine you have a friend's phone number written on a secret sticky note
hidden under your desk. When someone asks "What's your friend's number?", you have to crawl under the desk to find it.
If you just wrote the number in your address book (an explicit FK), anyone could look it up instantly.

## What it flags

Flags public reference navigation properties between `DbSet` entity types when the dependent entity does not expose a matching foreign-key property and the relationship is not otherwise configured.

## Why it matters

Shadow foreign keys make it harder to set or inspect relationships without loading the navigation entity. Explicit FK properties are easier to serialize, update, and reason about during schema reviews.

## Typical fix

Add the concrete foreign-key property and align the relationship configuration so the model exposes the key directly. The code fix inserts `<NavigationName>Id` above the navigation, uses the principal entity's primary-key type when it can be found from conventions, real key attributes, or visible single-property Fluent `HasKey(...)` configuration, and keeps optional nullable navigations nullable for value-type keys. The key type is written as it must be spelled in the dependent entity's file, so a key type from a namespace that file does not import (for example a strongly typed ID) comes out namespace-qualified instead of as a bare name that would not compile.

## Safe shapes

LC027 does not report collection navigations, owned types configured with `OwnsOne` / `OwnsMany`, relationships configured with `HasForeignKey(...)`, navigations or FK properties annotated with `[ForeignKey]`, or conventional FK properties such as `CustomerId` / `CustomerTypeId`.

The Fluent configuration check includes direct chains and a single-assignment relationship-builder local:

```csharp
var relationship = builder.HasOne(o => o.Customer).WithMany(c => c.Orders);
relationship.HasForeignKey("CustomerShadowId");
```

One-to-one builder continuations are supported too:

```csharp
var relationship = builder.HasOne(o => o.Customer);
relationship.WithOne(c => c.Order).HasForeignKey("CustomerShadowId");
```

If the relationship-builder local is reassigned before `HasForeignKey(...)`, LC027 stays conservative because the configured navigation is ambiguous.

When a team intentionally uses a configured shadow FK, keep the Fluent configuration explicit. LC027 treats that as intentional model design and does not offer a fixer.

## Samples

See `samples/LinqContraband.Sample/Samples/LC027_MissingExplicitForeignKey/` for a focused example.

## The crime

```csharp
public sealed class Order
{
    public Customer Customer { get; set; } = default!;
}
```

## A better shape

```csharp
public sealed class Order
{
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = default!;
}
```

Optional relationships should keep the FK optional too:

```csharp
public sealed class Order
{
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
}
```
