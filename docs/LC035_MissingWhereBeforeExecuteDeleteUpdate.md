---
layout: default
title: "LC035: Missing Where Before ExecuteDelete or ExecuteUpdate"
---

# LC035: Missing Where Before ExecuteDelete or ExecuteUpdate

## What it flags

Flags EF Core `ExecuteDelete`, `ExecuteDeleteAsync`, `ExecuteUpdate`, and `ExecuteUpdateAsync` calls when the target query has no proven `Where` filter.

Bulk execute APIs are set-based. Calling them on a bare `DbSet` or unfiltered query can delete or rewrite every row in the table.

## The crime

```csharp
db.Users.ExecuteDelete();
db.Users.ExecuteUpdate(setters => setters.SetProperty(u => u.Name, "Archived"));
```

## Safer shapes

Filter the target rows before the bulk operation:

```csharp
db.Users
    .Where(u => u.Age < 18)
    .ExecuteDelete();
```

Use query syntax when that is clearer; LC035 still recognises the `where` clause:

```csharp
var inactiveUsers =
    from user in db.Users
    where !user.IsActive
    select user;

inactiveUsers.ExecuteUpdate(setters => setters.SetProperty(u => u.Status, "Inactive"));
```

Build a filtered local query and optionally narrow it further:

```csharp
var q = db.Users.Where(u => u.TenantId == tenantId);

if (archivedOnly)
{
    q = q.Where(u => u.IsArchived);
}

q.ExecuteDelete();
```

## Local reassignment rules

LC035 treats a local query as filtered only when every visible path to the bulk call is filtered.

This stays quiet because the unconditional base assignment is filtered and the later conditional assignment only adds another filter:

```csharp
var q = db.Users.Where(u => u.TenantId == tenantId);
if (flag) q = q.Where(u => u.Id < 100);
q.ExecuteDelete();
```

This reports because the catch path can replace the filtered query with an unfiltered one:

```csharp
var q = db.Users.Where(u => u.TenantId == tenantId);

try
{
    q = q.Where(u => u.Id < 100);
}
catch
{
    q = db.Users;
}

q.ExecuteUpdate();
```

Earlier conditional assignments do not matter once a later unconditional filtered assignment overwrites the local before the bulk call.

## What counts as a filter

LC035 recognises:

- LINQ `Queryable.Where` and `Enumerable.Where` in the receiver chain.
- Query-syntax `where` clauses.
- Filtered local query initializers.
- Straight-line filtered local reassignments.
- Optional filtered narrowings after a filtered base local.

Project-local methods merely named `Where` do not count as proven filters.

## What it does not flag

LC035 only binds EF Core bulk execute methods from the real `Microsoft.EntityFrameworkCore` namespace. Same-name helpers in project-local or lookalike namespaces stay quiet.

There is no automatic fixer. Adding a predicate speculatively would be unsafe; the correct filter depends on tenant, lifecycle, audit, or business rules that the analyzer cannot infer.

## Manual review checklist

1. Is the target tenant, account, or lifecycle state filtered before the bulk operation?
2. Can any conditional, catch, loop, or switch path replace the query with an unfiltered one?
3. Is a project-local helper named `Where` hiding an unfiltered query?
4. Should this operation be guarded by a transaction, audit record, or explicit suppression comment?
