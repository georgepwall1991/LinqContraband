# Spec: LC009 - Missing AsNoTracking in Read Path

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
Add `.AsNoTracking()` to the query.

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

### When it stays quiet (non-goals)
- The query already opts a tracking mode in: `AsNoTracking()`, `AsNoTrackingWithIdentityResolution()`, or an explicit `AsTracking()`.
- The query contains a `Select(...)` projection — a projection to a non-entity shape is not tracked anyway.
- The enclosing method returns `IQueryable<T>` (deferred execution — the caller owns the tracking decision).
- The source is an `IQueryable<T>`/`DbSet<T>` **parameter or local** (ambiguous origin — the caller may use it for writes).
- A write is detected in the same executable body (`SaveChanges`/`SaveChangesAsync`, or `Add`/`AddRange`/`Update`/`Remove`/`RemoveRange` on a `DbSet`/`DbContext`).
- A **property of the materialized result is mutated** in the same body — on the result local, a `foreach` iteration variable over it, or inline on the materializer (`db.Users.First(...).Name = x`, compound assignment and `++`/`--` included). A mutation implies the entity is on a write path even when the `SaveChanges` lives in a helper the analyzer cannot see, and suggesting `AsNoTracking()` would break that cross-method save. Mutating an unrelated object (a DTO being populated from the entity) does not count.

## Code Fix
The fixer inserts `AsNoTracking()` directly on the EF source it found, using the semantic type rather than syntax so it places the call correctly for both shapes:

```csharp
db.Users.Where(...).ToList()        ->  db.Users.AsNoTracking().Where(...).ToList()
db.Set<User>().Where(...).ToList()  ->  db.Set<User>().AsNoTracking().Where(...).ToList()
```

(A purely syntactic walk could not tell the `Set<T>()` source invocation apart from a `.Where(...)` operator and would mis-place `AsNoTracking()` onto the `DbContext`.)

### When AsNoTracking is *not* safe
`AsNoTracking()` is a behaviour change, not just a perf tweak — apply the fix only on genuinely read-only paths:

- **Identity resolution.** No-tracking queries do not de-duplicate entity instances. A query that `Include`s a collection (or otherwise returns the same entity more than once) yields multiple distinct instances. Use `AsNoTrackingWithIdentityResolution()` when a single shared instance per entity matters.
- **Deferred / cross-method mutation.** A *local* mutation of the materialized entity now suppresses the rule (see above), but if the entity is returned untouched and a **caller** mutates and saves it, that remains invisible — `AsNoTracking()` would silently drop the change. The diagnostic is `Info` precisely because this cross-method case cannot be proven locally.
- **Re-attach / explicit state.** If the entity is later `Attach`ed or has `Entry(entity).State` set for an update, it must be tracked — do not add `AsNoTracking()`.
