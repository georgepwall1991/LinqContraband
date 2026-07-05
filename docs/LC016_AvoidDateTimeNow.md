---
layout: default
title: "Spec: LC016 - Avoid DateTime.Now in Queries"
---

# Spec: LC016 - Avoid DateTime.Now in Queries

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

The fixer chooses a unique local name when `now` is already used by an enclosing method, local function, or lambda parameter.
When the same clock property appears multiple times in one query lambda, LC016 reports it once and the fixer replaces each identical access in that lambda.
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

LC016 only reports clock members inside `IQueryable` expressions. In-memory `IEnumerable` filtering is outside the rule because the expression is already running in process.

The fixer does not introduce an injected clock service, change `Now` to `UtcNow`, or rewrite the query to a provider-specific server-clock function. Those choices affect application architecture and time-zone semantics, so they remain manual.

## Analyzer Logic

### ID: `LC016`
### Category: `Performance`
### Severity: `Warning`
