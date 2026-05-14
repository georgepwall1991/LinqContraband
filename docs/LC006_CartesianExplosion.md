# Spec: LC006 - Cartesian Explosion Risk

## Goal
Detect sibling collection `Include` paths on the same query without an effective `AsSplitQuery()`.

## The Problem
When a single SQL query joins to two or more sibling collections of the same root, the result set is the Cartesian product of those collections. For a User with 10 Orders and 10 Roles, a single-query join returns 100 rows for that one User instead of 20 — the duplicated columns are then projected back into navigation lists in memory. Multiply across a paged root set and the wire traffic, server memory, and EF Core fixup cost can dominate the query.

A linear nested path such as `Users → Orders → Items` is *not* sibling and *not* reported by LC006. Joining through a single chain still produces row duplication proportional to the deepest collection, but EF Core's translator and the rule both treat this as one path, not a Cartesian.

### Example Violation
```csharp
// Explosion Risk: result rows = |Users| * |Orders| * |Roles|
var users = db.Users
    .Include(u => u.Orders)
    .Include(u => u.Roles)
    .ToList();
```

### The Fix
Add `.AsSplitQuery()` so EF Core issues each collection load as its own SQL statement:

```csharp
var users = db.Users
    .AsSplitQuery()
    .Include(u => u.Orders)
    .Include(u => u.Roles)
    .ToList();
```

The provided code fix inserts `.AsSplitQuery()` immediately before the first `Include`. If the chain ends in `.AsSingleQuery()` (an explicit Cartesian opt-in), the fixer rewrites that call site to `.AsSplitQuery()` instead.

## When AsSplitQuery is Not a Free Win

`AsSplitQuery()` trades one risk for another. Before accepting the default fix, weigh:

- **Extra roundtrips.** A two-collection include becomes three SQL statements (root + each collection). On high-latency links the wall-clock cost can exceed the Cartesian-bandwidth cost it avoids.
- **Per-statement plan cost.** Each split statement is planned and executed independently. The root must be re-correlated for every child query, so a small root set with a small Cartesian explosion may legitimately prefer the single join.
- **Snapshot consistency.** A single-query include sees one consistent snapshot of the joined tables. Split queries run as separate statements, so a concurrent write between the root query and a child collection load can produce a fixup that no single-statement read would have observed. For most workloads this is acceptable; for read-modify-write paths it is not.
- **Transaction scope.** A single-query include is one statement and one implicit transaction; split queries are multiple statements. If callers need a single explicit transaction around the load, they must open one themselves before the materializing call — EF Core does not introduce one for `AsSplitQuery()` reads on their behalf.

## When Cartesian is Legitimate (and LC006 Should Be Suppressed)

LC006 is a precision-tightened heuristic, not a security rule. There are real shapes where leaving the single Cartesian query in place is the right call:

- **Small, bounded sibling sets** where the duplication is capped by domain constraints (e.g. each User has at most a handful of Roles).
- **Single-row roots** — `db.Users.Where(u => u.Id == id).Include(...)` — where the root cardinality is one and the Cartesian only multiplies the two child collections.
- **Bulk export / ETL** workloads where a single fast scan beats N+1 split statements on the throughput you care about.
- **Pre-aggregated read models** where the projection downstream collapses the duplication anyway.

In these cases either suppress the diagnostic with `#pragma warning disable LC006` on the local line, or assert the intent with `.AsSingleQuery()` (LC006 will still warn — the warning is a *reminder* that the Cartesian is deliberate, not a blocker).

## Analyzer Logic

LC006 resolves lambda, filtered, and literal string include paths when the navigation symbols are provable. It deduplicates repeated include paths and reports once for each risky query chain. A final or chain-prefix explicit `AsSplitQuery()` suppresses the diagnostic; a final explicit `AsSingleQuery()` keeps it active.

### ID: `LC006`
### Category: `Performance`
### Severity: `Warning`

### Notes
LC006 fires only when **two or more sibling collection navigations** are loaded under the same root or the same `ThenInclude` parent without an effective `AsSplitQuery()`. Reference navigations (single-row foreign-key targets) never cause Cartesian inflation and never trigger LC006, even when two of them appear as siblings. Sibling collections include arrays and any `IEnumerable<T>` shape; `string` is explicitly excluded. Repeated identical include paths are collapsed before the sibling check, so `Include(u => u.Orders).Include(u => u.Orders)` is a single path, not a Cartesian pair.

## Rule Boundary

- **vs LC028** (deep `ThenInclude` chain): LC028 fires on chain *depth* past a configured threshold. LC006 fires on sibling *width*. A `Users → Orders → Items → Reviews → Photos` chain is LC028's concern, not LC006's.
- **vs LC038** (excessive eager loading): LC038 fires on the *total count* of include paths past a configured threshold (the "loading too much in one query" smell). LC006 fires on the specific Cartesian shape, regardless of how many includes the chain contains.
- **vs unresolvable string Includes**: LC006 cannot prove a navigation is a collection when the path is a runtime string (`Include(navigation)` with a `string` local). It stays quiet rather than guess.
- **`AsSingleQuery()` semantics**: an explicit `AsSingleQuery()` at the end of a sibling-collection chain still triggers LC006. The warning is a deliberate reminder that the developer is opting into the Cartesian behaviour the rule exists to surface — not an analyzer false positive.

## Test Cases

### Violations
```csharp
db.Users.Include(u => u.Orders).Include(u => u.Roles);                 // two sibling collections
db.Users.Include(u => u.Orders).Include(u => u.Roles).Include(u => u.Tags); // three reported once
db.Users.AsSingleQuery().Include(u => u.Orders).Include(u => u.Roles);  // explicit single-query opt-in
db.Users
    .Include(u => u.Orders).ThenInclude(o => o.Comments)
    .Include(u => u.Orders).ThenInclude(o => o.Tags);                   // sibling ThenIncludes under Orders
```

### Valid
```csharp
db.Users.Include(u => u.Address).Include(u => u.Roles);                 // reference + collection, only one collection
db.Users.Include(u => u.Address).Include(u => u.Profile);               // two references, never Cartesian
db.Users.Include(u => u.Orders).ThenInclude(o => o.Items);              // linear chain, single path
db.Users.AsSplitQuery().Include(u => u.Orders).Include(u => u.Roles);   // explicit split
db.Users.Include(u => u.Orders).Include(u => u.Roles).AsSplitQuery();   // trailing split also accepted
db.Users.Include(u => u.Orders).Include(u => u.Orders);                 // repeated path, deduplicated
db.Users.Include(navigation).Include("Roles");                          // unresolved string, stays quiet
```
