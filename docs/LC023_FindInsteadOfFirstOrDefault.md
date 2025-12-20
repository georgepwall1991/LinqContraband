# Spec: LC023 - Suggest Find/FindAsync for Primary Key Lookups

## Goal
Detect usage of `FirstOrDefault(x => x.Id == id)` or `SingleOrDefault(x => x.Id == id)` and suggest using `Find(id)` or `FindAsync(id)` instead.

## The Problem
`FirstOrDefault` always executes a database query. In contrast, `Find` first checks the local change tracker. If the entity is already loaded, `Find` returns it without hitting the database. This can provide a significant performance boost in scenarios where the same entity is requested multiple times in the same context (e.g., in a complex transaction or nested logic).

### Example Violation
```csharp
// Violation: Always hits the database
var user = db.Users.FirstOrDefault(u => u.Id == userId);
```

### The Fix
Use `Find()` or `FindAsync()`.

```csharp
// Correct: Checks local cache first
var user = db.Users.Find(userId);
```

## Analyzer Logic

### ID: `LC023`
### Category: `Performance`
### Severity: `Info` (or `Warning`?) - I'll go with `Performance` warning.

### Algorithm
1.  **Target Methods**: Intercept invocations of:
    -   `FirstOrDefault`, `FirstOrDefaultAsync`
    -   `SingleOrDefault`, `SingleOrDefaultAsync`
    -   `First`, `FirstAsync`
    -   `Single`, `SingleAsync`
2.  **Receiver Check**: Ensure it's called on a `DbSet` or an `IQueryable` that is directly from a `DbSet`.
3.  **Predicate Analysis**: 
    -   Check if the method has a lambda predicate.
    -   Analyze the predicate to see if it's a simple equality check: `x => x.PrimaryKey == someValue`.
    -   Use `AnalysisExtensions.TryFindPrimaryKey` to identify the primary key property.
4.  **Exceptions**:
    -   If the query has other operators like `Include`, `AsNoTracking`, etc., `Find` cannot be used directly as it returns the tracked entity and doesn't support fluent chaining in the same way.
    -   *Constraint*: Only suggest if the chain is `db.Entities.FirstOrDefault(...)`.

## Test Cases

### Violations
```csharp
db.Users.FirstOrDefault(x => x.Id == 1);
db.Users.SingleAsync(x => x.Id == id);
```

### Valid
```csharp
db.Users.Find(1);
db.Users.Include(x => x.Profile).FirstOrDefault(x => x.Id == 1); // Complex query, Find not suitable
db.Users.AsNoTracking().FirstOrDefault(x => x.Id == 1); // Find always tracks
```

## Implementation Plan
1.  Create `LC023_FindInsteadOfFirstOrDefault` directory.
2.  Implement `FindInsteadOfFirstOrDefaultAnalyzer`.
3.  Implement tests.
