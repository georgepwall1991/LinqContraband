# Spec: LC029 - Redundant Identity Select

## Goal
Detect usage of `Select(x => x)` on an `IQueryable`.

## The Problem
`Select(x => x)` is redundant as it returns the identity of the object. While logically harmless, it adds unnecessary noise to the expression tree and can occasionally prevent the query provider from performing certain SQL optimizations.

### Example Violation
```csharp
// Violation: Identity select is redundant
var users = db.Users.Select(u => u).ToList();
```

### The Fix
Remove the `.Select(x => x)` call.

```csharp
// Correct
var users = db.Users.ToList();
```

## Analyzer Logic

### ID: `LC029`
### Category: `Performance`
### Severity: `Info`

### Algorithm
1.  **Target Method**: Intercept invocations of `Select`.
2.  **Lambda Check**: Inspect the lambda argument.
3.  **Identity Check**: If the lambda is an identity function (e.g., `x => x`), report a violation.

## Test Cases

### Violations
```csharp
query.Select(x => x);
```

### Valid
```csharp
query.Select(x => x.Name);
```

## Shipped Behavior

LC029 reports identity projections such as `Select(x => x)` on queryable chains. The fixer removes the redundant projection while preserving the rest of the fluent query.
