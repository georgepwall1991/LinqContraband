# Spec: LC035 - Missing Where Before ExecuteDelete or ExecuteUpdate

## Goal
Detect bulk delete or update calls that target an unfiltered entity set.

## The Problem
`ExecuteDelete()` and `ExecuteUpdate()` are set-based commands. Calling them on a bare `DbSet` can wipe or rewrite an entire table in one statement.

### Example Violation
```csharp
db.Users.ExecuteDelete();
db.Users.ExecuteUpdate(setters => setters.SetProperty(u => u.Name, "Archived"));
```

### Safer Shape
Filter the target rows first.

```csharp
db.Users.Where(u => u.Age < 18).ExecuteDelete();
```

## Analyzer Logic

### ID: `LC035`
### Category: `Safety`
### Severity: `Info`

### Notes
This rule is advisory only. There is no automatic fixer because adding a predicate speculatively would be unsafe. LC035 recognizes semantic LINQ filters in the direct fluent chain, query-syntax `where` clauses, simple local query initializers such as `var filtered = db.Users.Where(...); filtered.ExecuteDelete();`, and straight-line local reassignments such as `query = query.Where(...);`. Project-local methods merely named `Where` do not count as a proven filter, and conditional reassignments remain conservative.

LC035 only binds EF Core bulk execute methods from the real `Microsoft.EntityFrameworkCore` namespace. Same-name helpers in project-local or lookalike namespaces stay quiet.
