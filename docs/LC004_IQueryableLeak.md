---
layout: default
title: "Spec: LC004 - IQueryable to IEnumerable Leak"
---

# Spec: LC004 - IQueryable to IEnumerable Leak

## Goal
Detect when an `IQueryable` is passed to a method parameter of type `IEnumerable` and the callee is proven to force in-memory execution.

## The Problem
`IQueryable` represents a database query that hasn't run yet. `IEnumerable` usually represents an in-memory collection. Passing an `IQueryable` into an `IEnumerable` parameter erases provider semantics. If the callee then iterates, counts, or materializes that parameter, the remaining work happens in your app instead of in the database.

### Example Violation
```csharp
public void ProcessUsers(IEnumerable<User> users) { ... }

// Leak: ProcessUsers is proven to enumerate the sequence in memory.
ProcessUsers(db.Users); 
```

### The Fix
Change the method parameter to `IQueryable` when the callee should keep composing the database query. Explicitly call `.ToList()` at the call site when the boundary is intentional and the rest of the work should run in memory.

```csharp
// Correct: Allows composing the query inside the method
public void ProcessUsers(IQueryable<User> users) { ... }
```

```csharp
// Also correct: makes the client-side boundary explicit at the call site
ProcessUsers(db.Users.Where(user => user.IsActive).ToList());
```

## Analyzer Logic
- Reports only when the target method is inspectable in the current compilation.
- Treats a parameter as hazardous only when the method body proves one of these:
  - `foreach` or manual enumerator consumption.
  - terminal or materializing `Enumerable` calls such as `Any`, `Count`, `ToList`, or `ToArray`.
  - known BCL collection constructors such as `new List<T>(users)` or `new HashSet<T>(users)`.
  - forwarding into another same-compilation parameter already proven hazardous.
  - A C# query expression on the parameter (`from u in users where … select …`) is followed to its enumeration just like the fluent equivalent (`users.Where(...)`).
- Does not report for framework methods, delegate invocations, parameters already typed as `IQueryable`, custom constructors, or callees without source bodies.

### Reported Forwarding Shapes
LC004 follows simple same-compilation forwarding chains when the forwarded parameter is eventually consumed:

```csharp
void Wrapper(IEnumerable<User> users)
{
    CountUsers(users);
}

int CountUsers(IEnumerable<User> users)
{
    return users.Count();
}

Wrapper(db.Users); // LC004
```

The rule also handles expression-bodied methods and query syntax:

```csharp
bool HasUsers(IEnumerable<User> users) => users.Any();

int CountAdults(IEnumerable<User> users)
{
    var adults =
        from user in users
        where user.Age >= 18
        select user;

    return adults.Count();
}
```

### Safe Boundaries
LC004 stays silent when the callee only returns, stores, or passes the parameter without proving local enumeration. It also stays silent when the argument is already materialized or the parameter remains `IQueryable`:

```csharp
IEnumerable<User> KeepDeferred(IEnumerable<User> users) => users;

IQueryable<User> ComposeMore(IQueryable<User> users)
{
    return users.Where(user => user.IsActive);
}

KeepDeferred(db.Users);       // no LC004: no proven enumeration here
ComposeMore(db.Users);        // no LC004: provider semantics are preserved
ProcessUsers(db.Users.ToList()); // no LC004: boundary is explicit
```

### Scope And Non-Goals
The analysis is intentionally local. It inspects source bodies available in the current compilation and does not guess about framework calls, delegate targets, external assemblies, or custom collection constructors. Operations inside nested local functions and lambdas are not treated as part of the outer method body; that conservative boundary avoids reporting on helper code that may never run.

LC004 also does not try to prove whether client-side work is business-correct. If the callee intentionally applies in-memory rules, materialize explicitly at the call site so reviewers can see the boundary and cost.

## Fixer Behavior
- Offers one safe fix when the source query is generic: materialize explicitly with `.ToList()`.
- Preserves named arguments and parenthesizes complex expressions before adding `.ToList()` when needed.
- Does not offer signature-changing fixes. Changing `IEnumerable<T>` to `IQueryable<T>` can be the better design, but it changes the callee contract and may affect other callers.

### ID: `LC004`
### Category: `Performance`
### Severity: `Warning`
