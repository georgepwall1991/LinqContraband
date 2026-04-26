# LC027: Missing Explicit Foreign Key Property

## What it flags

Flags public reference navigation properties between `DbSet` entity types when the dependent entity does not expose a matching foreign-key property and the relationship is not otherwise configured.

## Why it matters

Shadow foreign keys make it harder to set or inspect relationships without loading the navigation entity. Explicit FK properties are easier to serialize, update, and reason about during schema reviews.

## Typical fix

Add the concrete foreign-key property and align the relationship configuration so the model exposes the key directly. The code fix inserts `<NavigationName>Id` above the navigation, uses the principal entity's primary-key type when it can be found, and keeps optional nullable navigations nullable for value-type keys.

## Safe shapes

LC027 does not report collection navigations, owned types configured with `OwnsOne` / `OwnsMany`, relationships configured with `HasForeignKey(...)`, navigations or FK properties annotated with `[ForeignKey]`, or conventional FK properties such as `CustomerId` / `CustomerTypeId`.

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
