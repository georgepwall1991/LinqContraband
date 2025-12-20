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
### Severity: `Info` (I'll use `Warning` for consistency with project style, or `Info` if it's very minor. Let's stick to `Warning` as it's "contraband").

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

## Implementation Plan
1.  Create `LC029_RedundantIdentitySelect` directory.
2.  Implement `RedundantIdentitySelectAnalyzer`.
3.  Implement tests.
