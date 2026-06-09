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
This rule is advisory only. There is no automatic fixer because adding a predicate speculatively would be unsafe. LC035 recognizes semantic LINQ filters in the direct fluent chain, query-syntax `where` clauses, simple local query initializers such as `var filtered = db.Users.Where(...); filtered.ExecuteDelete();`, and straight-line local reassignments such as `query = query.Where(...);`. A local with a **conditional** reassignment is treated as filtered only when the unconditional base assignment is filtered **and** every later conditional reassignment also adds a filter — so the common "base filter + optional extra narrowing" shape stays quiet:

```csharp
var q = db.Users.Where(u => u.Id > 10);
if (flag) q = q.Where(u => u.Id < 100);   // every path is filtered
q.ExecuteDelete();                         // not reported
```

while a conditional path that reassigns to an unfiltered query still reports. Project-local methods merely named `Where` do not count as a proven filter.

LC035 only binds EF Core bulk execute methods from the real `Microsoft.EntityFrameworkCore` namespace. Same-name helpers in project-local or lookalike namespaces stay quiet.
