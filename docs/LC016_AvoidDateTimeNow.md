---
layout: default
title: "LC016: Avoid DateTime.Now in Queries"
description: "LC016 flags DateTime.Now, UtcNow and DateTimeOffset.Now inside EF Core LINQ queries. Hoist the value into a local for cacheable, testable queries."
---

# LC016: Avoid DateTime.Now in Queries

## In Plain Terms

Imagine baking a cake. If the recipe says "Bake for 30 minutes," you can use
it every day. But if the recipe says "Bake until the clock shows exactly 4:03 PM on Tuesday," you can only use it once,
and then you have to write a new recipe.

## Goal
Detect usage of `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, or `DateTimeOffset.UtcNow` directly inside LINQ queries.

## The Problem
Using `DateTime.Now` inside a query prevents the database execution plan from being cached effectively because the value changes constantly. It also makes your queries harder to unit test because they depend on the system clock.

### Example Violation
```csharp
// Un-cacheable query
var activeUsers = db.Users.Where(u => u.ExpiryDate > DateTime.Now).ToList();
```

### The Fix
Extract the date to a local variable before running the query.

```csharp
// Correct: Cachable query
var now = DateTime.Now;
var activeUsers = db.Users.Where(u => u.ExpiryDate > now).ToList();
```

The fixer picks a name nothing else in the method uses: not a local, parameter, pattern, `out`, `foreach` or `catch` variable anywhere in the member, and not a field, property or other symbol in scope, so the new local never clashes with an enclosing block (CS0136) or silently shadows a field. Fix All applies the fixes one after another, so two fixes in the same declaration space, such as neighbouring `case` sections, declare `now` and `now1` rather than `now` twice.
When the same clock property appears multiple times in one query lambda or one query expression, LC016 reports it once and the fixer replaces each identical access there.
When the query sits in an embedded statement such as `if (flag) return ...;`, the fixer wraps it in a block with the new local. It offers no fix for a clock read in a `while`, `do` or `for` condition or a `for` incrementor, because the loop evaluates those on every pass and a hoisted value would never advance.
For expression-bodied methods and local functions that compose or materialize a query, the fixer converts the arrow body to a block, captures the clock value first, and then either returns the rewritten expression or keeps it as an expression statement for `void` and async non-generic task members. Other expression-bodied members remain manual because converting properties or indexers can change accessor shape and API style. Static query lambdas also remain manual because an extracted local would be an invalid capture.

## Guidance

Capture a single application-clock value before composing the query when the query should use "now" from your service process. The captured local is easier to assert in tests, easier to replace with an injected clock, and gives the query a stable parameter instead of embedding a fresh clock access inside the expression tree.

```csharp
var cutoff = clock.UtcNow.AddDays(-30);
var staleOrders = db.Orders.Where(o => o.UpdatedAt < cutoff).ToList();
```

Prefer `UtcNow` for persisted timestamps unless the model deliberately stores local time. If the business rule needs a database-server clock, use an explicit provider-supported database function or SQL expression instead of hiding that choice behind `DateTime.Now` in a LINQ predicate.

Modern EF Core providers differ in how they translate clock members, and captured values may be parameterized rather than inlined. LC016 is still useful because the direct clock access makes the query harder to reason about, harder to test, and provider-sensitive. Treat the warning as a prompt to choose the clock boundary deliberately.

## Non-Goals

LC016 only reports clock members inside `IQueryable` expressions. In-memory `IEnumerable` filtering is outside the rule because the expression is already running in process. The same goes for an `IQueryable` that provably wraps an in-memory collection, such as `list.AsQueryable()` or `new EnumerableQuery<T>(...)` in a unit test or fake repository. `AsQueryable()` over an `IEnumerable<T>`, an `IQueryable` parameter or a `DbSet` still reports.

The fixer does not introduce an injected clock service, change `Now` to `UtcNow`, or rewrite the query to a provider-specific server-clock function. Those choices affect application architecture and time-zone semantics, so they remain manual.

## Analyzer Logic

### ID: `LC016`
### Category: `Performance`
### Severity: `Warning`
