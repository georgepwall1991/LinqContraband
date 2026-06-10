# Spec: LC012 - Optimize Bulk Delete with ExecuteDelete

## Goal
Suggest using `ExecuteDelete()` instead of `RemoveRange()` for bulk deletions.

## The Problem
`RemoveRange()` requires you to fetch all entities into memory before marking them for deletion. For large sets of data, this is very slow. `ExecuteDelete()` (available in EF Core 7+) performs a direct SQL `DELETE` on the database without loading any data into memory.

### Example Violation
```csharp
var oldLogs = db.Logs.Where(l => l.Date < oneYearAgo);
db.Logs.RemoveRange(oldLogs);
```

### The Fix
Use `ExecuteDelete()`.

```csharp
// Fast: Direct SQL execution
db.Logs.Where(l => l.Date < oneYearAgo).ExecuteDelete();
```

## Analyzer Logic

### ID: `LC012`
### Category: `Performance`
### Severity: `Warning`

### When it fires
LC012 is conservative. It reports only when:
- the project exposes an EF Core `ExecuteDelete()` extension from the real `Microsoft.EntityFrameworkCore` namespace (so project-local or lookalike helpers do not enable the rule),
- the single `RemoveRange(...)` argument is still query-shaped (`IQueryable<T>` / `DbSet<T>`), and
- no *relevant* `SaveChanges()` / `SaveChangesAsync()` follows later in the same executable body. A later save is relevant unless it provably cannot commit these removals: a save on a **provably different context instance** — both receivers resolve, through single-assignment alias chains, to two different freshly-created (`new`) locals — and a save in a **mutually exclusive `if`/`else` or `switch` branch** (with no `goto case`/`goto default` in the switch) do not suppress the report.

### When it stays quiet (non-goals)
- Materialized lists or arrays (e.g. `...ToList()`) and tracked entity collections — the fetch already happened, so there is nothing to optimize away.
- Mixed or multiple `RemoveRange(query, entity)` `params` arguments — no single `ExecuteDelete()` replacement preserves that call shape.
- Custom or lookalike `ExecuteDelete()` helpers outside the EF Core namespace, and EF Core versions where `ExecuteDelete()` is unavailable.
- Calls followed by `SaveChanges()` / `SaveChangesAsync()` later in the same executable body on the same or a possibly-aliasing context — replacing deferred tracked deletion with immediate `ExecuteDelete()` would change unit-of-work timing. Aliases resolve through single-assignment chains (`var db2 = db1;` saves through `db2` still suppress); context parameters, fields, factory results, and reassigned locals can never be proven distinct and conservatively suppress. When the `DbSet` arrives as a parameter/local its owning context is unknowable, so any later save suppresses; a `RemoveRange` in a `try` with a save in a `catch` also stays quiet, because the try may throw *after* the removals were registered and the catch-side save would still commit them; a `switch` containing `goto case`/`goto default` keeps suppressing because sections can flow into each other.

## Code Fix

The fixer rewrites `context.RemoveRange(query)` to a direct bulk delete and prepends a warning comment (`// Warning: ExecuteDelete bypasses change tracking and cascades.`):

| Context | Rewrite |
| --- | --- |
| Synchronous method / lambda / local function | `query.ExecuteDelete();` |
| Async method / async lambda / async local function (with `ExecuteDeleteAsync` available) | `await query.ExecuteDeleteAsync();` |
| Async context where no `ExecuteDeleteAsync` overload exists | No fix offered |

The async branch matters: emitting a synchronous `ExecuteDelete()` inside an async method would inject a blocking, sync-over-async database call — the exact smell `LC008` flags. The fixer therefore prefers the awaited `ExecuteDeleteAsync()` overload when the **nearest enclosing function** is `async`, and declines entirely rather than introduce a blocking call when only the synchronous overload is available. "Nearest enclosing function" is deliberate: a synchronous local function nested inside an async method still receives the synchronous rewrite, because `await` would be illegal there.

### Safety contract
`ExecuteDelete()` / `ExecuteDeleteAsync()` issue a single SQL `DELETE` and **bypass EF Core change tracking, client-side cascades, save interceptors, and `SavingChanges` events**. They also execute immediately rather than deferring to the next `SaveChanges()`, which changes unit-of-work timing. The analyzer and fixer both decline the later-`SaveChanges` shape for this reason, but they cannot see a `SaveChanges()` that lives in a different method. Apply the optimization only after confirming change tracking, cascades, interceptors, and deferred-save semantics are not required for the deleted rows.
