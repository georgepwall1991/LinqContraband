---
layout: default
title: "LC003: Prefer Any() Over Count() Existence Checks"
---

# LC003: Prefer Any() Over Count() Existence Checks

## What It Flags

LC003 reports `IQueryable` existence checks that use `Count`, `LongCount`, `CountAsync`, or `LongCountAsync` when `Any` or `AnyAsync` communicates the same intent more directly.

```csharp
if (db.Users.Count(u => u.Active) > 0) // LC003
{
    // ...
}
```

```csharp
var hasUsers = await db.Users.LongCountAsync() != 0; // LC003
```

```csharp
return db.Users.Count() == 0; // LC003
```

## Why It Matters

For relational providers, `Any()` is normally translated as an existence query such as `EXISTS` or a single-row probe. The database can stop after the first qualifying row.

`Count()` asks for the full cardinality. When the result is only compared with zero or one to answer "does at least one row exist?", counting every matching row is unnecessary work and can force more scanning, aggregation, and memory pressure on large datasets.

## Safer Shapes

Use `Any()` when the question is "are there any rows?"

```csharp
if (db.Users.Any(u => u.Active))
{
    // ...
}
```

Use `!Any()` when the question is "are there no rows?"

```csharp
return !db.Users.Any();
```

Use `AnyAsync()` for EF Core async queries.

```csharp
var hasUsers = await db.Users.AnyAsync();
```

## Patterns

These existence checks report:

```csharp
query.Count() > 0;
0 < query.Count();
query.Count() >= 1;
1 <= query.Count();
query.Count() != 0;
0 != query.Count();
query.Count() == 0;
0 == query.Count();
await query.CountAsync() == 0;
await query.LongCountAsync() != 0;
```

The rule is not limited to `if` statements. It also reports in boolean assignments, return expressions, ternary conditions, and other scalar expression contexts.

```csharp
var hasAny = query.Count() > 0;      // LC003
return query.Count() == 0;          // LC003
var state = query.Count() != 0 ? 1 : 0; // LC003
```

## When Count Is Correct

Keep `Count()` when the actual total matters.

```csharp
var total = await db.Users.CountAsync();
```

Keep `Count()` for thresholds that cannot be expressed as a simple existence check.

```csharp
if (db.Users.Count() > 1)
{
    // Need at least two rows, not just one.
}
```

```csharp
if (db.Users.Count() >= 2)
{
    // Also not equivalent to Any().
}
```

The rule deliberately stays quiet for those thresholds.

## Scope

LC003 only targets `IQueryable` sources. It does not report `List<T>.Count`, array lengths, or in-memory `IEnumerable<T>` counts because those APIs have different cost models and may already be cheap or precomputed.

It recognises the LINQ `Queryable.Count`/`LongCount` methods and EF Core `CountAsync`/`LongCountAsync` extension methods. Provider-specific SQL may vary, but the existence-vs-cardinality intent is provider-independent.

## Fix Strategy

The fixer rewrites the comparison expression:

- `Count() > 0`, `Count() >= 1`, and `Count() != 0` become `Any()`.
- `Count() == 0` becomes `!Any()`, including named constants that fold to zero.
- Async count methods become `await AnyAsync()`.
- Predicate counts keep their predicate: `Count(x => x.Active) > 0` becomes `Any(x => x.Active)`.

The fixer changes only the existence-check expression. It does not rewrite cases where the numeric count result is used for display, pagination, thresholds above one, or any business rule that needs the exact total.
