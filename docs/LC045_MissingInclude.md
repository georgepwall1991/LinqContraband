# Spec: LC045 - Missing Include: navigation accessed on materialized entity

## Goal
Detect the canonical EF Core read-side bug: a DbSet-rooted query is materialized (`ToList`, `FirstOrDefault`, …) and a navigation property of the result is then read without a matching `Include`/`ThenInclude` in the chain. With lazy-loading proxies every access fires an extra query (the classic N+1); without proxies the navigation is silently `null` or an empty collection. Both ship invisibly and only surface as production slowness or missing data.

## The Problem
EF Core never loads navigations implicitly when the entity is materialized eagerly. The query below compiles, runs, and looks correct — but `o.Customer` was never loaded:

### Example Violation
```csharp
var orders = db.Orders.ToList();
foreach (var o in orders)
{
    Console.WriteLine(o.Customer.Name); // N+1 per order with proxies; NullReferenceException without
}
```

### Fixes
Eagerly load the navigation:
```csharp
var orders = db.Orders.Include(o => o.Customer).ToList();
foreach (var o in orders)
{
    Console.WriteLine(o.Customer.Name);
}
```
…or project exactly the data the code needs (often the better query):
```csharp
var rows = db.Orders.Select(o => new { o.Id, CustomerName = o.Customer.Name }).ToList();
```

## Code Fix
The code fix inserts the missing eager load immediately before the materializer — `recv.ToList()` becomes `recv.Include(x => x.Nav).ToList()`. Nested paths become `Include`/`ThenInclude` chains: a flagged `Customer.Address` produces `.Include(x => x.Customer).ThenInclude(x => x.Address)`. FixAll applies the same navigation across the document/project.

## Analyzer Logic

### ID: `LC045`
### Category: `Reliability`
### Severity: `Warning`

### Algorithm
1. **Anchor**: register on entity-producing materializers only — `ToList`/`ToArray` (+`Async`) and `First`/`Single`/`Last` (`OrDefault`, `Async`) framework methods. Aggregates (`Count`, `Any`, …) never materialize entities and are ignored.
2. **Chain proof**: walk the receiver chain back to a `DbSet<T>` property/field on a `DbContext`. Only shape-preserving operators are allowed (`Where`, `OrderBy*`, `Skip`, `Take`, `Distinct`, `AsNoTracking*`, `AsTracking`, `AsSplitQuery`, `AsSingleQuery`, `TagWith*`, `IgnoreQueryFilters`, `Include`, `ThenInclude`). Anything else — `Select`, `Join`, `GroupBy`, custom extensions — bails. The chain may be hoisted across a single-assignment local (`var q = db.Orders.Where(…); q.ToList()`).
3. **Included paths**: parse every `Include`/`ThenInclude` (lambda, filtered-lambda, and constant-string overloads) into navigation paths and record every prefix (`Include(o => o.A.B)` covers `A` and `A.B`). If any Include cannot be parsed (dynamic string), the whole query is skipped — it could cover anything.
4. **Navigation classification**: a property is a navigation when its type (or collection element type) has a `DbSet` on the same context. Owned and unmapped types have no `DbSet` and are never flagged.
5. **Usage scan**: resolve the result into a single-assignment local (or a direct inline access for single-entity materializers) and track entity-bearing locals — `foreach` iteration variables and indexer-initialized locals. Record each navigation read, including nested reference-navigation paths (`o.Customer.Address` → `Customer.Address`).
6. **Emit**: one diagnostic per distinct missing navigation path, at the first access site, carrying the materializer location and the dotted path for the code fix. Only maximal paths are reported — fixing `Customer.Address` eagerly loads `Customer` too.

## False-Positive Disciplines
- Any non-shape-preserving operator in the chain (`Select`, `Join`, custom extensions) silences the query.
- The result (or any entity drawn from it) escaping — returned, passed as an argument (including `db.Entry(e)`), captured by a lambda, or stored outside a local — silences the query: a helper might explicitly load the navigation.
- A reassigned result local silences the query.
- Navigation writes are not reads: `o.Customer = c` and `o.Items.Add(x)` are recognized relationship-fix-up patterns and stay quiet.
- Properties whose type has no `DbSet` (owned/unmapped types) are never navigations.
- Non-EF sources (`List<T>` LINQ) never match the DbSet root proof.

## Deliberate Decisions & Known Limits
- **Null-guarded access still fires.** `if (o.Customer != null)` is flagged on purpose: with proxies the null check itself triggers the N+1 load, and without proxies the navigation is always null, so the guard is dead code hiding the bug. Suppress with `#pragma warning disable LC045` if the guard is intentional.
- Model-level `AutoInclude()` configuration is invisible to the analyzer; if you rely on it, suppress LC045 at the access site or project instead.
- v1 scope is intra-procedural and local-based. Out of scope (quiet, not flagged): second-level access through collection navigations (`foreach (var i in o.Items) i.Product`), accesses inside lambdas over the result, `Entry(...).Reference/Collection(...).Load()` recognition, `IQueryable` parameters or repository-returned queries as roots, and `foreach` directly over an inline materializer.

## Test Cases

### Violations
```csharp
var orders = db.Orders.ToList();
foreach (var o in orders) Console.WriteLine(o.Customer.Name);   // LC045: Customer

var order = db.Orders.FirstOrDefault();
Console.WriteLine(order.Customer.Name);                          // LC045: Customer

var withCustomer = db.Orders.Include(o => o.Customer).ToList();
foreach (var o in withCustomer) Console.WriteLine(o.Customer.Address.City); // LC045: Customer.Address
```

### Valid
```csharp
var orders = db.Orders.Include(o => o.Customer).ToList();
foreach (var o in orders) Console.WriteLine(o.Customer.Name);

var names = db.Orders.Select(o => o.Customer.Name).ToList();     // projection — out of scope

var list = db.Orders.ToList();
return list;                                                     // escapes — caller may hydrate
```
