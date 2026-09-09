---
layout: default
title: "Spec: LC048 - Lost Update Risk"
---

# Spec: LC048 - Lost Update Risk

## Goal

Detect a tracked Entity Framework Core single-entity read-modify-write whose new scalar value depends on the value that was loaded and which can reach `SaveChanges` on the same `DbContext` without proven optimistic-concurrency protection.

## The problem

A tracked query loads a snapshot of a row. If two requests load the same value, both compute a replacement, and both save, the later update can silently overwrite the earlier one:

```csharp
var order = await db.Orders.SingleAsync(order => order.Id == id);
order.Quantity += amount; // LC048
await db.SaveChangesAsync();
```

The diagnostic is placed on the mutated property expression (`order.Quantity`). The matching `SaveChanges` or `SaveChangesAsync` call is attached as an additional diagnostic location so tooling can show the complete read-modify-write path.

## Manual remediation

Choose the protection that matches the application's data and conflict semantics.

### Add optimistic concurrency

Configure a row-version or another concurrency token and handle `DbUpdateConcurrencyException` according to a deliberate retry, reload, merge, or rejection policy:

```csharp
public sealed class Order
{
    public int Id { get; set; }
    public int Quantity { get; set; }

    [Timestamp]
    public byte[] Version { get; set; } = null!;
}
```

Direct Fluent `IsRowVersion()` configuration is recognised as entity-wide protection. `[ConcurrencyCheck]` or Fluent `IsConcurrencyToken()` is recognised when it protects the property being mutated. These configurations add original values to the update predicate, so a competing update causes zero affected rows and EF Core throws instead of silently accepting an overwrite.

### Make the database update atomic

For arithmetic or other expressions that the provider can translate, avoid loading the old value and perform one set-based update:

```csharp
await db.Orders
    .Where(order => order.Id == id)
    .ExecuteUpdateAsync(setters => setters
        .SetProperty(order => order.Quantity, order => order.Quantity + amount));
```

This is usually the clearest option when no in-memory merge or tracked aggregate behavior is required. Check the affected-row count when the operation also needs existence or business-state validation.

### Use an explicit transaction strategy

When several reads and writes must form one unit, use an explicit EF Core transaction with an isolation level and retry policy chosen for the provider and workload. LC048 treats `BeginTransaction*` and `UseTransaction*` as an intentional transaction boundary. That suppression is not a claim that every isolation level prevents lost updates; the transaction design remains the application's responsibility.

## Analyzer logic

### ID: `LC048`
### Category: `Reliability`
### Severity: `Warning`

LC048 runs per method and reports only when each part of the path is statically proven:

1. A stable context origin is a `DbContext` parameter, a readonly `DbContext` field, `this` in a context method, or a stable local alias of one of those origins. Computed context properties are not assumed stable.
2. A tracked `DbSet<TEntity>` or `DbContext.Set<TEntity>()` query is followed through known shape-preserving LINQ and EF Core operators.
3. `First*`, `Single*`, `Last*`, or `Find*`, including their async forms, materializes one entity. The entity and context may then flow through stable local aliases.
4. A scalar property is updated with `++`, `--`, a compound assignment, a self-read assignment such as `order.Quantity = order.Quantity + amount`, or a same-property state transition guarded by a condition that reads the loaded property.
5. A later `SaveChanges` or `SaveChangesAsync` on the same proven context is reachable from the mutation.

Recognised shape-preserving operators include `Where`, ordering, `Skip`, `Take`, `Distinct`, `Reverse`, `AsQueryable`, tracking-mode operators, `Include` / `ThenInclude`, query-filter and auto-include controls, split/single-query controls, tags, and EF Core raw-SQL query roots. `AsNoTracking` and `AsNoTrackingWithIdentityResolution` mark the result untracked; a later `AsTracking` restores tracked status.

The analyzer also summarises source-visible private methods in the same syntax tree. It can connect a mutation of a direct entity parameter and a save through a direct context parameter back to stable arguments at the caller. A transaction API inside such a helper makes that helper an intentional boundary.

## Safe cases that stay quiet

LC048 does not report:

- a blind scalar replacement that does not derive from the loaded value;
- a mutation with no later reachable save, or a save proven to use another context;
- an entity loaded through `AsNoTracking` or `AsNoTrackingWithIdentityResolution` unless tracking is explicitly restored;
- an entity protected by `[Timestamp]` or direct Fluent `IsRowVersion()` configuration, including inherited timestamp attributes;
- a mutated property protected by `[ConcurrencyCheck]` or direct Fluent `IsConcurrencyToken()` configuration;
- a method containing a recognised explicit EF Core `BeginTransaction*` or `UseTransaction*` call;
- an atomic `ExecuteUpdate` / `ExecuteUpdateAsync`, because no tracked entity read-modify-write is involved;
- LINQ, context, transaction, save, or builder lookalikes outside the recognised framework namespaces.

## Intentional limits

The analysis is deliberately conservative. It does not guess through projections such as `Select`, custom query operators, repository-returned query abstractions, computed context properties, reassigned locals, arbitrary object graphs, or non-private/cross-file helper contracts. It recognises direct Fluent concurrency configuration on the configured entity type; opaque configuration helpers and runtime model conventions are outside the static proof. It tracks single-entity terminal operations rather than collection materialization and later iteration.

The save-flow proof is intra-method and lexical with control-flow reachability checks. It does not attempt whole-program alias analysis, infer database isolation from connection configuration, or prove that an application's transaction level prevents lost updates. These boundaries favor actionable diagnostics over guesses in code where entity identity, tracking, or context ownership is ambiguous.

## Why remediation is not automated

There is no behavior-preserving universal rewrite. Adding a concurrency token changes the model and database schema and requires a conflict policy. Replacing tracked code with `ExecuteUpdate` changes tracking, interceptors, domain behavior, and affected-row handling. Introducing a transaction changes isolation, locking, retry, lifetime, and throughput characteristics. LC048 therefore provides a warning and evidence locations but leaves the remedy to the application author.
