---
layout: default
title: "Spec: LC045 - Missing Include: navigation accessed on materialized entity"
---

# Spec: LC045 - Missing Include: navigation accessed on materialized entity

## Goal
Detect the canonical EF Core read-side bug: a DbSet-rooted query is materialized (`ToList`, `FirstOrDefault`, …), or synchronously enumerated directly with `foreach`, and a navigation property of the entity is then read without a matching `Include`/`ThenInclude` in the chain. With lazy-loading proxies the access can fire an extra query (the classic N+1); without lazy loading, and when explicit loading, `AutoInclude`, or relationship fix-up has not populated it, the navigation can remain `null` or empty. Both failure modes can ship invisibly and surface only as production slowness or missing data.

## The Problem
EF Core can populate navigations through eager, explicit, lazy, or model-level automatic loading, and through relationship fix-up for already-tracked entities. Without one of those mechanisms, materializing an entity does not load arbitrary navigations. The query below compiles, runs, and looks correct — but `o.Customer` was not requested:

### Example Violation
```csharp
var orders = db.Orders.ToList();
foreach (var o in orders)
{
    Console.WriteLine(o.Customer.Name); // N+1 with proxies; otherwise may be null when no other loading mechanism applies
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
The code fix wraps the exact query source immediately before the materializer or direct `foreach` enumeration — `recv.ToList()` becomes `recv.Include(x => x.Nav).ToList()`, while `foreach (var x in recv)` becomes `foreach (var x in recv.Include(x => x.Nav))`. Nested paths become `Include`/`ThenInclude` chains: a flagged `Customer.Address` produces `.Include(x => x.Customer).ThenInclude(x => x.Address)`. FixAll applies the same navigation across the document/project.

The fix only registers when the source expression it would wrap is statically `IQueryable<T>`. If the query has already been widened to `IEnumerable<T>` (for example `IEnumerable<Order> source = db.Orders; source.ToList()`), LC045 still reports the missing eager load, but the remediation is manual: add `Include` before widening or keep the local typed as `IQueryable<T>`.

## Analyzer Logic

### ID: `LC045`
### Category: `Reliability`
### Severity: `Warning`

### Algorithm
1. **Anchor**: register on entity-producing materializers — `ToList`/`ToArray`/`ToHashSet` (+ supported `Async` forms), `First`/`Single`/`Last` (`OrDefault`, `Async`), and query-root `ElementAt` (`OrDefault`, supported `Async`) — plus synchronous `foreach` directly over a statically `IQueryable<T>` DbSet-rooted source. Inline collection materializers use the same source proof. Aggregates (`Count`, `Any`, …) never materialize entities and are ignored.
2. **Chain proof**: walk the semantic source parameter back to a `DbSet<T>` property/field on a `DbContext`, or to `DbContext.Set<TEntity>()`. Exact `Queryable`, EF, and relational symbols preserve only known query shapes, including `AsQueryable`, `IgnoreAutoIncludes`, and EF Core `FromSql*`; reordered static arguments are resolved by parameter ordinal. Anything else — `Select`, `Join`, `GroupBy`, custom extensions, and lookalikes — bails.
3. **Included paths**: parse every `Include`/`ThenInclude` (lambda, filtered-lambda, and constant-string overloads) into navigation paths and record every prefix (`Include(o => o.A.B)` covers `A` and `A.B`). If any Include cannot be parsed (dynamic string), the whole query is skipped — it could cover anything.
4. **Navigation classification**: a property is a navigation when its type (or collection element type) has a `DbSet` on the same context. Owned and unmapped types have no `DbSet` and are never flagged.
5. **Usage scan**: build and cache the Roslyn CFG for the containing method or constructor, then analyse forward from the materializer or direct loop source. Track entity-bearing locals — collection results, `foreach` iteration variables, indexer-initialized locals, exact `System.Linq.Enumerable` element extraction (`First*`, `Single*`, `Last*`, `ElementAt*`) from a materialized collection, aliases, and locals extracted from reference navigations — by origin, binding generation, and navigation prefix. Nested collection iteration carries that prefix (`order.Items` then `item.Product` → `Items.Product`). At joins, keep only identical bindings, retain navigation writes that occurred on every incoming path, and treat an escape or uncertain reassignment on any incoming path as uncertainty for subsequent reads. Exact `List<T>.ForEach` and single-source `Enumerable.Where`/`Select`/`Any`/`All` inline callbacks receive their own nested CFG only while the original materialized collection generation is proven active at the call. `Where` forwards provenance only through an effect-free inline predicate, and scalar `Select` projections do not poison later ordinary reads; entity-returning projections, arbitrary callbacks, and delegate/method-group forms remain boundaries. Direct property-subpattern reads use the same navigation-path and dominance proof.
6. **Emit**: one diagnostic per distinct missing navigation path, at the first access site, carrying the exact query source location and the dotted path for the code fix. Only maximal paths are reported — fixing `Customer.Address` eagerly loads `Customer` too.

## False-Positive Disciplines
- Any non-shape-preserving operator in the chain (`Select`, `Join`, custom extensions) silences the query.
- A result or extracted entity that escapes — returned, passed as an argument (including `db.Entry(e)`), captured by a lambda, or stored outside a local — makes only subsequent reads of that origin uncertain: a helper might explicitly load the navigation. Proven reads before the escape still report. Escaping one extracted entity does not poison a sibling origin, while escaping their materialized collection root makes every still-root-derived origin uncertain.
- Reassigning a result local or repointing an entity local similarly suppresses only subsequent reads whose origin is no longer proven. If only one control-flow branch escapes or repoints the value, the merged origin is uncertain and stays quiet afterward.
- Navigation writes are not reads: `o.Customer = c` (including compound, `??=`, and deconstruction assignments) and `o.Items.Add(x)` are recognized relationship-fix-up patterns and stay quiet. A navigation write satisfies a later read only for the same entity origin and only when every path reaching that read performs the write; a one-branch write or a write to a different extracted entity does not suppress the diagnostic.
- Mid-path casts and null-forgiving operators in Include lambdas (`Include(o => o.Customer!.Address)`, `Include(o => ((Derived)o.Nav).Child)`) parse as the full path; an Include shape the parser cannot prove silences the whole query.
- `nameof(o.Customer)` evaluates nothing and is never flagged.
- Properties whose type has no `DbSet` (owned/unmapped types) are never navigations.
- Non-EF sources (`List<T>` LINQ) never match the DbSet root proof.

## Deliberate Decisions & Known Limits
- **Null-guarded access still fires.** `if (o.Customer != null)` and `order?.Customer` are flagged on purpose: with proxies the null check itself can trigger the N+1 load, and without another loading mechanism a consistently null navigation makes the guard dead code hiding the bug. Suppress with `#pragma warning disable LC045` if the guard is intentional. This holds for every null-conditional spelling: chained inline access on the materializer (`FirstOrDefault()?.Customer?.Name`, `FirstOrDefault()?.Customer.Address?.City`), parenthesized regrouping (`(order?.Customer)?.Address?.City`, reported as `Customer.Address`, including inline materializer and inherited-navigation forms), conditional element access on the result (`orders?[0].Customer`), and locals initialized from a conditional indexer (`var o = orders?[0];`). Conditional method-call results such as `(order?.Customer.GetDetached())?.Address` are treated as call results, not as a continuation of the queried navigation path.
- Model-level `AutoInclude()` configuration is invisible to the analyzer; if you rely on it, suppress LC045 at the access site or project instead.
- Widened `IEnumerable<T>` aliases are diagnostic-only: the analyzer can still prove the DbSet root, but the fixer will not emit `Include` against a non-queryable source expression.
- Current scope is intra-procedural and local-based (methods and constructors). Out of scope (quiet, not flagged): `await foreach`, arbitrary callbacks and delegate/method-group consumers, predicate/default-value element extractor overloads, custom extraction lookalikes, provider-specific temporal APIs, `Find*`, `Entry(...).Reference/Collection(...).Load()` recognition, and `IQueryable` parameters / repository-returned queries as roots.

## Test Cases

### Violations
```csharp
var orders = db.Orders.ToList();
foreach (var o in orders) Console.WriteLine(o.Customer.Name);   // LC045: Customer

var order = db.Orders.FirstOrDefault();
Console.WriteLine(order.Customer.Name);                          // LC045: Customer

var withCustomer = db.Orders.Include(o => o.Customer).ToList();
foreach (var o in withCustomer) Console.WriteLine(o.Customer.Address.City); // LC045: Customer.Address

var nested = db.Orders.ToList();
foreach (var order in nested)
foreach (var item in order.Items) Console.WriteLine(item.Product.Name);      // LC045: Items.Product
```

### Valid
```csharp
var orders = db.Orders.Include(o => o.Customer).ToList();
foreach (var o in orders) Console.WriteLine(o.Customer.Name);

var nested = db.Orders.Include(o => o.Items).ThenInclude(i => i.Product).ToList();
foreach (var order in nested)
foreach (var item in order.Items) Console.WriteLine(item.Product.Name);

var names = db.Orders.Select(o => o.Customer.Name).ToList();     // projection — out of scope

var list = db.Orders.ToList();
return list;                                                     // no navigation read before the escape
```
