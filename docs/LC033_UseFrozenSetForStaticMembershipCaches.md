---
layout: default
title: "LC033: Use FrozenSet for Read-Only Static Membership Caches"
description: "LC033 flags private static readonly HashSet lookup caches that are never mutated and suggests FrozenSet on .NET 8+ for faster membership checks."
---

# LC033: Use FrozenSet for Read-Only Static Membership Caches

## In Plain Terms

Imagine you made a VIP guest list and then laminated it forever. You do not
need an editable whiteboard anymore. A laminated list is faster to check and nobody can accidentally scribble on it.

## Goal
Detect `private static readonly HashSet<T>` membership caches that can be safely converted to `FrozenSet<T>` on .NET 8+.

## The Problem
`HashSet<T>` is mutable and optimized for general-purpose set operations. When a cache is initialized once, never mutated, and only used for `Contains(...)`, `FrozenSet<T>` is a better fit: it trades construction cost for faster steady-state lookups and lower ongoing overhead.

### Example Violation
```csharp
private static readonly HashSet<string> ElevatedRoles = new(StringComparer.OrdinalIgnoreCase)
{
    "admin",
    "ops"
};

static bool IsElevated(string role) => ElevatedRoles.Contains(role);
```

### The Fix
Convert the cache to `FrozenSet<T>` and build it once with `ToFrozenSet(...)`.

```csharp
private static readonly FrozenSet<string> ElevatedRoles = new string[]
{
    "admin",
    "ops"
}.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
```

## Analyzer Logic

### ID: `LC033`
### Category: `Performance`
### Severity: `Info`

### Algorithm
1. Require a source-declared `private static readonly HashSet<T>` field with a single declarator and an inline initializer.
2. Require `System.Collections.Frozen.FrozenSet<T>` and the `ToFrozenSet(...)` extension to be available in the compilation: from the framework's `System.Collections.Frozen.FrozenSet` class on .NET 8+ (or the `System.Collections.Immutable` 8.0+ package), or from a source-declared polyfill in the `System.Collections.Frozen` namespace. On frameworks without them (.NET 7 and older, .NET Standard without the package) the rule stays silent.
3. Accept only fixer-safe initializer shapes:
   - collection initializer forms (`new HashSet<T>() { ... }`, optionally with a comparer),
   - `new HashSet<T>(source[, comparer])`,
   - `source.ToHashSet([comparer])`.
4. Track every source reference to the field across the compilation and require every usage to be a direct `Contains(...)` call.
5. Skip any field used in `IQueryable` / expression-tree contexts, passed around through aliases, mutated, enumerated, or touched through any non-`Contains` member.

## Notes
This rule is intentionally narrow. If there is any ambiguity about initialization, mutability, or usage shape, it stays silent instead of suggesting a speculative rewrite.
