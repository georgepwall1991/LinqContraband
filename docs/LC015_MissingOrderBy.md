# Spec: LC015 - Ensure OrderBy Before Skip/Take

## Goal
Detect usages of `Skip()`, `Take()` (via the unordered-pagination operator family), `Last()`, `LastOrDefault()`, or `Chunk()` on an `IQueryable` that has not been ordered. Without an explicit ordering, the database is free to return rows in any order, so pagination, "last item" lookups, and chunked enumeration become non-deterministic.

## The Problem

When you paginate data ("get page 2") or ask for the "last" item, you implicitly assume the data is sorted. If it is not, the database is free to use any plan that returns the requested row count — and the chosen plan can change between runs as statistics update, the buffer cache warms, or parallelism kicks in. The user-visible failures:

1. **Flaky pagination**: the same row appears on page 1 and page 3, or a row is silently skipped — the page-window depends on whatever order the storage engine chose to surface rows in.
2. **Non-repeatable "last" lookups**: `Last()` returns different rows on consecutive runs, especially across a deploy that changes the plan.
3. **Chunked enumeration drift**: `Chunk(n)` produces partitions whose membership changes between iterations.

The rule also flags **misplaced** `OrderBy` calls that happen *after* pagination (e.g. `.Skip(10).OrderBy(...)`). This is almost always a bug: it sorts only the returned page, not the entire dataset, so the page boundary was already non-deterministic.

### Example Violations
```csharp
// 1. Unordered pagination: which 10 rows get skipped?
var page2 = db.Users.Skip(10).Take(10).ToList();

// 2. Misplaced sort: takes an arbitrary 10 rows, then sorts that page.
var page3 = db.Users.Take(10).OrderBy(u => u.Name).ToList();

// 3. Non-deterministic "last".
var newest = db.Events.LastOrDefault();
```

### The Fix
Always order the query before pagination, "last", or chunked enumeration.

```csharp
var page2 = db.Users.OrderBy(u => u.Id).Skip(10).Take(10).ToList();
var newest = db.Events.OrderByDescending(e => e.OccurredAt).LastOrDefault();
```

## How the Code Fix Decides

LC015 ships an EF Core-aware code fix that inserts `.OrderBy(x => x.<key>)` immediately before the unordered operator — but it is intentionally conservative. The fix only registers when the entity type's primary key is unambiguous:

- A single property carrying `[Key]` from `System.ComponentModel.DataAnnotations` (including keys declared on a base type).
- A single property literally named `Id` (case-insensitive).
- A single property named `<EntityType>Id` (case-insensitive), the EF Core convention for entity-prefixed primary keys.

The fix **does not** register when:

- The entity has two or more `[Key]`-annotated properties (composite key). A partial-key `OrderBy` would not produce deterministic pagination, which is exactly the failure LC015 exists to surface.
- No `[Key]` attribute is present and the entity has no `Id` or `<EntityType>Id` property (e.g. configured purely via Fluent API, owned types, projections, anonymous types, value tuples).
- The reported operator is the **misplaced** variant (`OrderBy` after `Skip`/`Take`). Inserting another `OrderBy` would preserve the wrong query shape; the user has to move the existing call instead.

When the fix does not register, the analyzer still reports the diagnostic. Treat the warning as a prompt to pick the right stable key by hand, using the guidance below.

## Picking a Stable Key in the No-Fix Case

The "stable" in *stable ordering* is domain-specific — the analyzer cannot know what stability means for your data. Patterns that work well:

- **Primary key first.** A surrogate `Id` or `<EntityType>Id` is the safest default, even when the user thinks they want chronological order — primary keys never tie and never change.
- **Composite keys: order by every key part.** `query.OrderBy(x => x.OrderId).ThenBy(x => x.LineNumber).Skip(...)`. Missing any part of the composite key reintroduces non-determinism.
- **Time-series: timestamp plus tiebreaker.** `OrderByDescending(e => e.OccurredAt).ThenBy(e => e.Id)` — two events with the same timestamp need a stable tiebreaker, or pagination drifts at the seam.
- **Avoid floating-point and case-insensitive text sorts.** Both can produce different orderings under different collations or precision modes; pagination across them is fragile.
- **Do not rely on insert order, ROWID, or "the natural order".** Storage engines deliberately do not preserve insert order; relying on it is the original failure mode this rule catches.

For shapes where ordering genuinely does not matter (a one-shot bulk export, a `.Last()` over a single-row materialized view, a test fixture), suppress with `#pragma warning disable LC015` on the local line and a comment explaining the assumption.

## Analyzer Logic

### ID: `LC015`
### Category: `Reliability`
### Severity: `Warning`

### Notes
LC015 evaluates EF-backed `IQueryable<T>` chains where pagination or "last row" operators depend on a deterministic order, including simple local aliases assigned from `DbSet<T>` or `DbContext.Set<T>()` before pagination. It also follows aliases that already contain `OrderBy(...)`, `Skip(...)`, or `Take(...)`, so ordered locals do not warn and misplaced sorting after a paged local is still diagnosed. It stays quiet for explicit LINQ-to-Objects sources such as `new List<T>().AsQueryable()` because database row ordering is not involved.

The order must be established upstream of the reported operator; an `OrderBy` after `Skip` or `Take` still leaves the page selection non-deterministic.

## Rule Boundary

- **vs LC005** (multiple `OrderBy` calls): LC005 fires when consecutive `OrderBy` calls reset the sort. LC015 fires when *no* upstream ordering exists for a pagination/last/chunk operator. They can co-occur: `db.Users.OrderBy(x => x.Name).OrderBy(x => x.Id).Skip(10)` would trip LC005 (second `OrderBy` resets) but LC015 would stay quiet (an `OrderBy` is present).
- **vs LC023** (`Find` over `FirstOrDefault` on PK): LC023 looks for `FirstOrDefault(x => x.Id == ...)` patterns and suggests `Find`. LC015 cares only about ordering of pagination — the two rules can fire on the same query without overlap.
- **Misplaced-OrderBy diagnostic** is a separate `MisplacedRule` descriptor and is reported on the trailing `OrderBy` invocation itself; the fixer deliberately does not register for it because a mechanical "add another OrderBy" would entrench the bug.

## Test Cases

### Violations
```csharp
db.Users.Skip(10);
db.Users.Where(x => x.Active).Skip(5);
db.Users.Last();
db.Users.LastOrDefault();
db.Users.Select(x => x.Name).Chunk(10);
db.Users.Take(10).OrderBy(u => u.Name);              // misplaced OrderBy
```

### Valid
```csharp
db.Users.OrderBy(x => x.Id).Skip(10);
db.Users.OrderByDescending(x => x.Date).Last();
db.Users.OrderBy(x => x.Id).Where(x => x.Active).Skip(10);            // order preserved through Where
db.Users.OrderBy(x => x.Id).Select(x => x.Name).Skip(10);             // order preserved through projection
var ordered = db.Users.OrderBy(x => x.Id); ordered.Skip(10);          // ordered alias
new List<User>().AsQueryable().Skip(10);                              // LINQ-to-Objects, no DB ordering involved
```
