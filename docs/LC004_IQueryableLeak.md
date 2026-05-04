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
Change the method parameter to `IQueryable` if you want to allow further filtering, or explicitly call `.ToList()` at the call site if you intend to fetch the data.

```csharp
// Correct: Allows composing the query inside the method
public void ProcessUsers(IQueryable<User> users) { ... }
```

## Analyzer Logic
- Reports only when the target method is inspectable in the current compilation.
- Treats a parameter as hazardous only when the method body proves one of these:
  - `foreach` or manual enumerator consumption.
  - terminal or materializing `Enumerable` calls such as `Any`, `Count`, `ToList`, or `ToArray`.
  - known BCL collection constructors such as `new List<T>(users)` or `new HashSet<T>(users)`.
  - forwarding into another same-compilation parameter already proven hazardous.
- Does not report for framework methods, delegate invocations, parameters already typed as `IQueryable`, custom constructors, or callees without source bodies.

## Fixer Behavior
- Offers one safe fix when the source query is generic: materialize explicitly with `.ToList()`.
- Does not offer signature-changing fixes.

### ID: `LC004`
### Category: `Performance`
### Severity: `Warning`
