---
layout: default
title: "LC009: Missing AsNoTracking in Read Path"
description: "LC009 suggests AsNoTracking() for read-only EF Core queries, so the change tracker does not snapshot entities the code never modifies."
---

# LC009: Missing AsNoTracking in Read Path

## In Plain Terms

Imagine you go to a museum. You promise not to touch anything. But security
guards still follow you and take high-resolution photos of every painting you look at, just in case you decide to draw a
mustache on one. It wastes their time and memory.

## Goal
Suggest using `AsNoTracking()` for queries that only read data and do not modify entities.

## The Problem
By default, EF Core tracks every entity it fetches so it can detect changes. This tracking process consumes CPU and memory. For read-only operations (like a search page or a dashboard), this overhead is wasted and slows down your application.

### Example Violation
```csharp
public List<User> GetActiveUsers()
{
    // Fetches and tracks users, even if we only display them
    return db.Users.Where(u => u.Active).ToList();
}
```

### The Fix
Add `.AsNoTracking()` to the query. Because this method returns the entities, the code fix is not offered here: check that no caller changes and saves them, then add it by hand. When the entities stay inside the method (displayed, counted, projected), the one-click fix is offered.

```csharp
public List<User> GetActiveUsers()
{
    // Fast read-only query
    return db.Users.AsNoTracking().Where(u => u.Active).ToList();
}
```

## Analyzer Logic

### ID: `LC009`
### Category: `Performance`
### Severity: `Info`

### When it fires
LC009 reports when a read-only query is materialized (`ToList`/`ToArray`/`First`/`Single`/`AsEnumerable`, sync or async, and friends) over an EF source with no tracking opt-out. The EF source is recognized both as:

- a `DbSet<T>` **property** (`db.Users.ToList()`), and
- a `DbSet<T>` **returned from a method**, most importantly the generic-repository `context.Set<T>()` read path (`db.Set<User>().ToList()`).

One query gets one report. In `db.Orders.ToList().Where(o => o.Total > 1).ToList()` or `(await db.Orders.ToListAsync()).Where(...).ToList()` the EF query runs at the inner `ToList()`/`ToListAsync()`, so that is where LC009 reports. The outer `ToList()` is LINQ to Objects over a list that is already loaded and is not reported again. The rule still follows the entities through that outer call: when its result is changed (for example in a `foreach` over it), the query is on a write path and stays quiet, and when it is returned or handed on, the report comes without a fix.

### When it stays quiet (non-goals)
- The query already opts a tracking mode in: `AsNoTracking()`, `AsNoTrackingWithIdentityResolution()`, or an explicit `AsTracking()`.
- The query contains a `Select(...)` projection — a projection to a non-entity shape is not tracked anyway.
- The enclosing method returns `IQueryable<T>` (deferred execution — the caller owns the tracking decision).
- The source is an `IQueryable<T>`/`DbSet<T>` **parameter or local** (ambiguous origin — the caller may use it for writes).
- A write is detected in the same executable body (`SaveChanges`/`SaveChangesAsync`, or `Add`/`AddRange`/`Update`/`Remove`/`RemoveRange` on a `DbSet`/`DbContext`).
- The **materialized entity is changed through its own API** in the same body: a method declared by the entity's type (`order.Ship()`, inline `db.Orders.First(...).Ship()` included), a change to a navigation collection (`order.Lines.Add(line)`, `Remove`, `Clear`, ...), a field write (`order.Status = "Cancelled"` on a public field), or the entity used as a mapping destination (`mapper.Map(dto, order)`, `patch.ApplyTo(order)`). Object members such as `ToString()` and methods of the result list itself (`orders.Add(...)`) are not entity changes.
- A **property of the materialized result is mutated** in the same body — on the result local (only when the materializer's value is stored directly into a single-assignment local), a nested member rooted in that result (`user.Profile.DisplayName = name`), a `foreach` iteration variable over it, or inline on the materializer (`db.Users.First(...).Name = x`, compound assignment and `++`/`--` included). A mutation implies the entity is on a write path even when the `SaveChanges` lives in a helper the analyzer cannot see, and suggesting `AsNoTracking()` would break that cross-method save. Mutating an unrelated object does not count: a DTO populated from entity values, a repointed local mutated while it held a different object, and indexer writes that replace a collection element (`users[0] = new User()`) all leave the rule firing.

## Code Fix
The fix is offered only when the materialized entities stay inside the method. When they leave it, the rule still reports but offers no fix, because code the analyzer cannot see may change and save them, and `AsNoTracking()` would turn that save into a silent no-op. The entities leave the method when the materializer, its result local, or a `foreach` variable over it is:

- returned or yielded (`return db.Users.ToList();`, `return user;`),
- passed as an argument (`Show(users)`, `mapper.Map(order, dto)`, `View(model)`),
- stored in a field, property, another local, an array, a tuple, or an object or anonymous-object initializer,
- handed to a delegate (`users.ForEach(u => ...)`), or
- carried through LINQ to Objects into one of the above (`return users.Where(...).ToList();`).

LINQ that no longer carries the entity stays local: `users.Count(...)`, `users.Any(...)` and `users.Select(u => u.Name).ToList()` keep the fix. For a reported query without a fix, check the callers first: if none of them changes and saves the entities, add `AsNoTracking()` by hand.

The fixer inserts `AsNoTracking()` directly on the EF source it found, using the semantic type rather than syntax so it places the call correctly for both shapes:

```csharp
db.Users.Where(...).ToList()        ->  db.Users.AsNoTracking().Where(...).ToList()
db.Set<User>().Where(...).ToList()  ->  db.Set<User>().AsNoTracking().Where(...).ToList()
```

(A purely syntactic walk could not tell the `Set<T>()` source invocation apart from a `.Where(...)` operator and would mis-place `AsNoTracking()` onto the `DbContext`.)

Some operators only accept a `DbSet<T>`: `FromSqlRaw`, `FromSql`, `FromSqlInterpolated`, the SQL Server `TemporalAll`/`TemporalAsOf`/... operators, and project helpers declared on `DbSet<T>`. `AsNoTracking()` returns an `IQueryable<T>`, so the fixer places it after the last of those operators in the chain instead of on the `DbSet` itself:

```csharp
db.Users.FromSqlRaw(sql, id).ToList()  ->  db.Users.FromSqlRaw(sql, id).AsNoTracking().ToList()
```

If such an operator does not return a query (a helper on `DbSet<T>` that returns a `List<T>`, say), `AsNoTracking()` has nowhere to go, so the rule reports without a fix.

The message names the method the query sits in. For a query inside a lambda (`Task.Run(() => db.Users.ToList())`) that is the method containing the lambda; inside a property getter it is the property, inside a constructor the type, and in a top-level program it reads `<top-level statements>`.

### When AsNoTracking is *not* safe
`AsNoTracking()` is a behaviour change, not just a perf tweak — apply the fix only on genuinely read-only paths:

- **Identity resolution.** No-tracking queries do not de-duplicate entity instances. A query that `Include`s a collection (or otherwise returns the same entity more than once) yields multiple distinct instances. Use `AsNoTrackingWithIdentityResolution()` when a single shared instance per entity matters.
- **Deferred / cross-method mutation.** A *local* change of the materialized entity suppresses the rule (see above). If the entity is returned or handed on untouched and a **caller** or callee changes and saves it, the analyzer cannot see that, so it reports without offering the fix. `AsNoTracking()` there would silently drop the change. The diagnostic is `Info` precisely because this cross-method case cannot be proven locally.
- **Re-attach / explicit state.** If the entity is later `Attach`ed or has `Entry(entity).State` set for an update, it must be tracked — do not add `AsNoTracking()`.
