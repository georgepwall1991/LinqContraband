# LC027: Missing Explicit Foreign Key Property

## What it flags

Flags relationship types that rely only on navigation properties because explicit foreign-key properties make mappings, updates, and troubleshooting more predictable.

## Why it matters

LinqContraband reports this rule when the query shape suggests a risky or non-translatable pattern that is better made explicit before it reaches production.

## Typical fix

Add the concrete foreign-key property and align the relationship configuration so the model exposes the key directly.

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
