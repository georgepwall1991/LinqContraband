---
layout: default
title: "LC035: Missing Where Before ExecuteDelete or ExecuteUpdate"
description: "LC035 flags EF Core ExecuteDelete and ExecuteUpdate calls with no proven Where filter, which delete or rewrite every row in the table."
---

# LC035: Missing Where Before ExecuteDelete or ExecuteUpdate

## In Plain Terms

Imagine you meant to erase one line on the whiteboard, but instead you
pressed the giant "erase whole board" button.

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

If a local has no unconditional base assignment, LC035 still stays quiet for a complete `if`/`else` where both branches definitely assign filtered queries before the bulk call. The same every-path rule applies to conditional receivers: ternary and switch-expression receivers are considered filtered only when every arm contains a real `Where(...)`.

Assignments inside a loop or `try` block that also holds the bulk call run before it on every path, so a filtered query built there is seen. This batched delete stays quiet whether `query` is declared before the loop, inside it, or inside a surrounding `try`:

```csharp
var query = db.PersistedGrants.Where(x => x.Expiration < now).OrderBy(x => x.Expiration);
while (found >= batchSize)
{
    found = await query.Take(batchSize).ExecuteDeleteAsync(ct);
}
```

An assignment later in the loop body reaches the bulk call on the next pass, so `query = db.PersistedGrants;` after the delete still reports. A `catch` that runs the bulk call does not count a filter applied in the `try` block, because the exception can come first.

## Queries passed into helpers

When the query comes from a parameter, the helper's own body cannot show whether it is filtered, so LC035 follows the parameter to the method's call sites in the compilation:

```csharp
public Task DeleteAsync(IEnumerable<int> ids)
{
    var query = _db.Set<T>().Where(m => ids.Contains(m.Id));
    return DeleteInternalAsync(query);
}

protected static async Task DeleteInternalAsync(IQueryable<T> query) => await query.ExecuteDeleteAsync();
```

- Every call site passes a filtered query (directly, through a filtered local, or through another helper's parameter whose callers filter): no report.
- A call site passes an unfiltered query: LC035 reports that argument, naming the helper, for example `DeleteInternalAsync(_db.Set<T>())`. An extension helper called as `db.Users.DeleteAll()` reports on `db.Users`.
- The callers cannot all be seen: LC035 reports the bulk call, as it did before. That is the case when the method has no call site in the compilation (public API called from elsewhere), is used as a method group or delegate, is `virtual`, `abstract`, an `override` or an interface implementation, or reassigns the parameter before the bulk call.

These reports need the whole compilation, so in the IDE they appear with full solution analysis or on build. A public helper whose callers in the compilation all filter stays quiet even though another assembly could call it with an unfiltered query.

A project helper that applies `Where` to its parameter and returns it counts as a filter where it is called:

```csharp
private static IQueryable<Grant> ApplyExpiredFilter(IQueryable<Grant> query) =>
    query.Where(g => g.Expiration < DateTime.UtcNow);

await ApplyExpiredFilter(db.Grants).ExecuteDeleteAsync();
```

Every value the helper returns must carry a `Where`; a helper that returns its parameter unchanged on some path is not a filter.

## What counts as a filter

LC035 recognises:

- LINQ `Queryable.Where` and `Enumerable.Where` in the receiver chain.
- Query-syntax `where` clauses.
- Filtered local query initializers.
- Straight-line filtered local reassignments.
- Optional filtered narrowings after a filtered base local.
- Complete filtered `if`/`else` assignments and all-filtered ternary or switch-expression receivers.
- Filtered locals declared before or inside a loop or `try` block that holds the bulk call.
- A query parameter whose every call site in the compilation passes a filtered query.
- A call to a project helper whose every `return` carries a `Where`, including a `virtual` helper such as VirtoCommerce's `BuildExpiredQuery(...)` (its visible body is what runs unless a subclass overrides it).

A project's own `Where` overload counts when it takes a predicate expression (`Expression<Func<TEntity, bool>>`) and the call passes a lambda, such as a `Where<T>(this DbSet<T>, Expression<Func<T, bool>>)` that forwards to `Queryable.Where`. Other methods merely named `Where`, such as one taking a string reason, do not count as proven filters.

## What it does not flag

LC035 only binds EF Core bulk execute methods from the real `Microsoft.EntityFrameworkCore` namespace. Same-name helpers in project-local or lookalike namespaces stay quiet.

There is no automatic fixer. Adding a predicate speculatively would be unsafe; the correct filter depends on tenant, lifecycle, audit, or business rules that the analyzer cannot infer.

## Manual review checklist

1. Is the target tenant, account, or lifecycle state filtered before the bulk operation?
2. Can any conditional, catch, loop, or switch path replace the query with an unfiltered one?
3. Is a project-local helper named `Where` hiding an unfiltered query?
4. Should this operation be guarded by a transaction, audit record, or explicit suppression comment?
