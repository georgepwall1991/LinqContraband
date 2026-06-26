---
layout: default
title: "Spec: LC029 - Redundant Identity Select"
---

# Spec: LC029 - Redundant Identity Select

## Goal
Detect usage of `Select(x => x)` on queryable or enumerable chains.

## The Problem
`Select(x => x)` is redundant as it returns the identity of the object. While logically harmless, it adds unnecessary noise to the expression tree and can occasionally prevent the query provider from performing certain SQL optimizations.

### Example Violation
```csharp
// Violation: Identity select is redundant
var users = db.Users.Select(u => u).ToList();
```

Statement-bodied identity lambdas are equivalent when the source is enumerable:

```csharp
// Violation: Still returns the same element unchanged
var users = cachedUsers.Select(u => { return u; }).ToList();
```

### The Fix
Remove the `.Select(x => x)` call.

```csharp
// Correct
var users = db.Users.ToList();
```

If the `Select` was being used as a marker for an intentional boundary, keep the boundary explicitly and remove only the identity projection:

```csharp
// Violation: the boundary is real, the identity projection is not
var users = db.Users.AsEnumerable().Select(u => u).ToList();

// Correct: the client-side boundary is still visible
var users = db.Users.AsEnumerable().ToList();
```

## Analyzer Logic

### ID: `LC029`
### Category: `Performance`
### Severity: `Info`

### Algorithm
1.  **Target Method**: Intercept invocations of `Select`.
2.  **Receiver Check**: Require a fluent `IQueryable<T>` or `IEnumerable<T>` receiver that the fixer can preserve directly.
3.  **Lambda Check**: Inspect the lambda argument, including delegate-created statement lambdas.
4.  **Type Check**: Require the selector return type to match the source parameter type, so casts such as `x => (object)x` are not treated as redundant.
5.  **Identity Check**: If the lambda is an identity function (for example `x => x` or `{ return x; }`), report a violation.

## Test Cases

### Violations
```csharp
query.Select(x => x);
items.Select(x => { return x; });
```

### Valid
```csharp
query.Select(x => x.Name);
```

### Intentional boundaries

Use APIs that state the boundary directly: `AsEnumerable()`, `AsAsyncEnumerable()`, `ToList()`, `ToArray()`, or a reviewed suppression when the full scan is intentional. LC029's fixer preserves the receiver, so `query.AsEnumerable().Select(x => x)` becomes `query.AsEnumerable()`.

## Shipped Behavior

LC029 reports identity projections such as `Select(x => x)` on queryable chains and `Select(x => { return x; })` on interface-shaped enumerable chains. The fixer removes the redundant projection while preserving the rest of the fluent query.

Static extension calls such as `Enumerable.Select(items, x => x)`, concrete enumerable receivers such as `List<T>`, awaited-task projections such as `items.Select(async x => await x)`, explicit-cast projections such as `items.Select<Base, Base>(x => (Derived)x)`, and type-changing projections such as `items.Select(x => (object)x)` are not reported because removing the call would either require a different rewrite shape or change the projected type/surface.

Fluent receivers remain supported when they are parenthesized, explicitly cast, or null-forgiven, so `(query).Select(x => x)`, `((IQueryable<User>)query).Select(x => x)`, and `query!.Select(x => x)` are still treated as redundant identity projections.
