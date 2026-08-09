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
The code fix wraps the exact query source immediately before the materializer or direct `foreach` enumeration — `recv.ToList()` becomes `recv.Include(x => x.Nav).ToList()`, while `foreach (var x in recv)` becomes `foreach (var x in recv.Include(x => x.Nav))`. Wrapping the source of an `await foreach` would leave an `IQueryable<T>` that is no longer an `IAsyncEnumerable<T>` (CS8415), so the rewrite restores the bridge: `await foreach (var x in db.Set)` becomes `await foreach (var x in db.Set.Include(x => x.Nav).AsAsyncEnumerable())`. When the stream already goes through `AsAsyncEnumerable()`, the `Include` simply lands before it. If the compilation has no `AsAsyncEnumerable`, the direct-`DbSet` async form reports without a fix rather than emitting code that does not build. Nested paths become `Include`/`ThenInclude` chains: a flagged `Customer.Address` produces `.Include(x => x.Customer).ThenInclude(x => x.Address)`. When the query already includes a prefix of the flagged path with a lambda overload, the fix extends that chain — `Include(o => o.Customer)` gains `.ThenInclude(x => x.Address)` rather than a second `Include(x => x.Customer)` — choosing the longest matching prefix and leaving later operators in place. String `Include` overloads return `IQueryable`, so nothing can be appended to them and the source is wrapped as before. In LINQ query syntax the fix goes on the expression the query draws from — `from o in db.Orders` becomes `from o in db.Orders.Include(x => x.Customer)` — because the query source the analyzer reports is the lowered identity projection, and wrapping that would emit `select o.Include(...)`, where the range variable is an entity rather than a queryable. A query with a continuation, or with a clause other than `where`/`orderby`, offers no fix rather than guessing; no such query is reported today, since `join`, a second `from` and `let` all lower to operators the chain proof rejects. A contract test holds the whole rule to this: for every shape LC045 reports, a fix must be offered, applying it must clear the diagnostic, and the result must **emit**. Adding a reportable shape means adding it to that corpus. FixAll applies the same navigation across the document/project.

The fix registers when the source expression it would wrap is statically `IQueryable<T>`, or when it is a never-reassigned local whose initializer is — in which case the `Include` goes on that initializer, so a query widened to `IEnumerable<T>` is fixed where it was assigned rather than where it is consumed.

## Analyzer Logic

### ID: `LC045`
### Category: `Reliability`
### Severity: `Warning`

### Algorithm
1. **Anchor**: register on entity-producing materializers — `ToList`/`ToArray`/`ToHashSet` (+ supported `Async` forms), `First`/`Single`/`Last` (`OrDefault`, `Async`), and query-root `ElementAt` (`OrDefault`, supported `Async`) — plus synchronous `foreach` directly over a DbSet-rooted source that is either statically `IQueryable<T>` or a local assigned such a query exactly once — widening the static type to `IEnumerable<T>` does not change what EF does when the loop runs, and the same loop over `source.ToList()` is already reported, plus `await foreach` over a proven EF stream: the exact `EntityFrameworkQueryableExtensions.AsAsyncEnumerable()` bridge over an `IQueryable<T>`, or a source that is still statically `IQueryable<T>` (a `DbSet<T>` is directly awaitable because it implements both interfaces). An arbitrary `IAsyncEnumerable<T>` is not a proven EF stream and stays quiet. Inline collection materializers use the same source proof. Aggregates (`Count`, `Any`, …) never materialize entities and are ignored.
2. **Chain proof**: walk the semantic source parameter back to a `DbSet<T>` property/field on a `DbContext`, or to `DbContext.Set<TEntity>()`. Exact `Queryable`, EF, and relational symbols preserve only known query shapes, including `AsQueryable`, `IgnoreAutoIncludes`, and EF Core `FromSql*`; reordered static arguments are resolved by parameter ordinal. An identity projection `Select(x => x)` is also shape preserving: it runs no user code and yields the very entities its source produced, which is exactly what LINQ query syntax lowers a trailing `select x` to, so `from o in db.Orders where o.Id > 0 select o` is proven like the method chain it compiles to (the query-comprehension wrapper is peeled). The selector must be an inline single-parameter lambda whose body is that same parameter and whose return type is the parameter's own type — an upcast, a rewrapping call, a method group, and the indexed `(x, i)` overload are all real projections. Anything else — `Select`, `Join`, `GroupBy`, custom extensions, and lookalikes — bails.
3. **Included paths**: parse every `Include`/`ThenInclude` (lambda, filtered-lambda, and constant-string overloads) into navigation paths and record every prefix (`Include(o => o.A.B)` covers `A` and `A.B`). If any Include cannot be parsed (dynamic string), the whole query is skipped — it could cover anything.
4. **Model-level eager loading**: cache exact top-level `OnModelCreating` overrides per context, matching constructed generic contexts through their original symbols. An exact EF `modelBuilder.Entity<TEntity>().Navigation(e => e.Nav).AutoInclude()` chain counts as loading only that entity/navigation path. The same proof follows an unconditional `modelBuilder.ApplyConfiguration(new TConfiguration())` into the source-visible implementation of the exact `IEntityTypeConfiguration<TEntity>.Configure` method, where direct top-level `builder.Navigation(...).AutoInclude()` settings are applied in execution order. Later fluent, standalone, or applied-configuration disablement removes the proof; conditional/runtime settings and unknown builder-consuming helpers invalidate it. A query-level `IgnoreAutoIncludes()` disables all model-level evidence for that query.
5. **Navigation classification**: a property is a navigation when its type (or collection element type) has a `DbSet` on the same context. Owned and unmapped types have no `DbSet` and are never flagged.
6. **Usage scan**: build and cache the Roslyn CFG for the containing method or constructor, then analyse forward from the materializer or direct loop source. Track entity-bearing locals — collection results, `foreach` iteration variables, indexer-initialized locals, exact `System.Linq.Enumerable` element extraction (`First*`, `Single*`, `Last*`, `ElementAt*`, `MinBy`/`MaxBy`) from a materialized collection or from an element-preserving view of it, including the filtered overloads whose predicate is an inline effect-free lambda, aliases, and locals extracted from reference navigations — by origin, binding generation, and navigation prefix. Iterating an element-preserving in-memory view of the materialized collection — exact `System.Linq.Enumerable` `Where`, `OrderBy`/`OrderByDescending`/`ThenBy`/`ThenByDescending`, `Skip`, `Take`, `Distinct`, `Reverse`, and `AsEnumerable`, chained in any order — carries the collection's origin, because those operators yield the same entity instances; callback-taking operators require an inline effect-free lambda, and such a view is not an escape. Nested collection iteration carries that prefix (`order.Items` then `item.Product` → `Items.Product`), including through an element-preserving in-memory view of the navigation collection — `order.Items.Where(i => i.Active)` yields the very instances `order.Items` holds, and the operator set and effect-free callback requirement are shared with the collection-level view proof. The same prefix carries into an inline callback over the navigation collection and into element extraction out of it: `order.Items.Sum(i => i.Product.Price)`, `order.Items.First(i => i.Id == id)` and `order.Items[0]` all read `Items.Product`, as does anything reached through a copy of the collection such as `order.Items.ToList()` or `order.Items.ToArray()` — a copy is a different collection holding the same entity instances. A copy of the materialized collection itself — `orders.ToList()`, `orders.ToArray()` — reads the same way. Because the query materializer is itself a `ToList`, a copy is only accepted where the source is already proven to be the materialized collection or a navigation of a tracked entity, neither of which the materializer's own `DbSet` source can satisfy. Indexing into such a copy — `orders.ToList()[0]` — reads the same way, because the receiver check walks the view or copy chain back to the result local. Handing the collection to an exact `Enumerable` extractor or an element-preserving view is not an escape of that navigation; handing it to a user helper still is. The callback parameter binds to a navigation origin derived from the receiver's own origin, so an escape of the parent entity makes those nested reads uncertain too, and the callback must be an inline effect-free lambda. At joins, keep only identical bindings, retain navigation writes that occurred on every incoming path, and treat an escape or uncertain reassignment on any incoming path as uncertainty for subsequent reads. Exact `List<T>.ForEach` and single-source `Enumerable` inline callbacks — `Where`, `Select`, `Any`, `All`, the ordering key selectors (`OrderBy`/`OrderByDescending`/`ThenBy`/`ThenByDescending`), the partition predicates (`SkipWhile`/`TakeWhile`), the aggregate callbacks (`Count`, `LongCount`, `Sum`, `Average`, `Min`, `Max`), the extraction predicates and key selectors, and the grouping callbacks (`ToDictionary`, `ToLookup`, `GroupBy`, `SelectMany`, `DistinctBy`) — receive their own nested CFG only while the original materialized collection generation is proven active at the call. Each is invoked once per element, so a navigation read inside any of them is a per-element read. `Select`, `Min` and `Max` hand their callback's result back to the caller, so an entity-returning callback still reports its own read but remains an escape for later ones. The grouping callbacks keep the entities in their result — a dictionary, a lookup, groupings, a flattened sequence — so they report the read inside the callback and stay an escape regardless of what the callback returns. `Where` forwards provenance only through an effect-free inline predicate, and scalar `Select` projections do not poison later ordinary reads; entity-returning projections, arbitrary callbacks, and delegate/method-group forms remain boundaries. Direct property-subpattern reads use the same navigation-path and dominance proof.
7. **Emit**: one diagnostic per distinct missing navigation path, at the first access site, carrying the exact query source location and the dotted path for the code fix. Only maximal paths are reported — fixing `Customer.Address` eagerly loads `Customer` too.

## False-Positive Disciplines
- Any non-shape-preserving operator in the chain (`Select`, `Join`, custom extensions) silences the query. The same holds for in-memory views: `orders.Select(...)`, a custom extension, a method-group callback, or an effectful `Where` predicate is a boundary, and only exact `Enumerable` element-preserving operators (`Where`, `SkipWhile`, `TakeWhile`, the orderings, `Skip`, `Take`, `Distinct`, `Reverse`, `AsEnumerable`) forward the collection's origin.
- A result or extracted entity that escapes — returned, passed as an argument (including `db.Entry(e)`), captured by a lambda, or stored outside a local — makes only subsequent reads of that origin uncertain: a helper might explicitly load the navigation. Proven reads before the escape still report. Escaping one extracted entity does not poison a sibling origin, while escaping their materialized collection root makes every still-root-derived origin uncertain.
- Reassigning a result local or repointing an entity local similarly suppresses only subsequent reads whose origin is no longer proven. If only one control-flow branch escapes or repoints the value, the merged origin is uncertain and stays quiet afterward.
- Navigation writes are not reads: `o.Customer = c` (including compound, `??=`, and deconstruction assignments) and `o.Items.Add(x)` are recognized relationship-fix-up patterns and stay quiet. EF's explicit loading is recorded as the same fact: `db.Entry(order).Reference(o => o.Customer).Load()` — and the `Collection`, string-named and `LoadAsync` forms — populates the navigation exactly as writing it would, so the rules below apply to it unchanged. A navigation write satisfies a later read only for the same entity origin and only when every path reaching that read performs the write; a one-branch write or a write to a different extracted entity does not suppress the diagnostic. A write is normally credited to the origin it was made on, but an unconditional write inside a loop over the whole collection is credited to the collection, so manual relationship fix-up — populating a navigation yourself and then reading it — is not reported. That requires the loop to iterate the collection itself rather than a filtered view, and nothing between the loop body and the write may skip it — including the loop body itself, since `foreach (var o in orders) if (c) o.Customer = x;` makes the `if` the body; a later escape or reassignment of the collection discards the fact. A read inside a callback body is covered too: the callback runs in its own control-flow graph, so the outer walk is asked what the collection holds by the time the call runs and the callback's reads are filtered against that.
- Mid-path casts and null-forgiving operators in Include lambdas (`Include(o => o.Customer!.Address)`, `Include(o => ((Derived)o.Nav).Child)`) parse as the full path; an Include shape the parser cannot prove silences the whole query.
- Model-level eager-loading evidence is context- and path-scoped. Conditional, deferred, runtime-valued, or early-exit-guarded `AutoInclude()` calls, shadowed or hidden-slot `OnModelCreating` lookalikes, fluent, standalone, or applied-configuration `AutoInclude(false)`, later base/helper/indirect configuration boundaries, and configuration belonging to another context, entity, or navigation never suppress LC045. Ordinary exact EF builder calls such as `HasKey` and relational mapping extensions do not invalidate an otherwise direct configuration proof.
- `nameof(o.Customer)` evaluates nothing and is never flagged.
- Properties whose type has no `DbSet` (owned/unmapped types) are never navigations.
- The same identity-projection proof applies to an in-memory view, so `from x in orders where x.Id > 0 select x` over a materialized collection reads exactly like `orders.Where(...)` does.
- **Naming the collection does not hide it.** A local given the materialized collection, or an element-preserving view or copy of it — `var active = orders.Where(o => o.Active);`, `var page = orders.ToList();`, `var alias = orders;`, and aliases of those in turn — stands in for the collection, so a loop, an element extraction, and a callback all read through the name exactly as they read through the collection. The local must be assigned exactly once before the read: a reassigned or conditionally bound local is not the collection. Because the alias *is* the collection, handing it to a helper is an escape of the collection and discards the proof for later reads, including reads through the original local.
- Non-EF sources (`List<T>` LINQ) never match the DbSet root proof.

## Deliberate Decisions & Known Limits
- **Null-guarded access still fires.** `if (o.Customer != null)` and `order?.Customer` are flagged on purpose: with proxies the null check itself can trigger the N+1 load, and without another loading mechanism a consistently null navigation makes the guard dead code hiding the bug. Suppress with `#pragma warning disable LC045` if the guard is intentional. This holds for every null-conditional spelling: chained inline access on the materializer (`FirstOrDefault()?.Customer?.Name`, `FirstOrDefault()?.Customer.Address?.City`), parenthesized regrouping (`(order?.Customer)?.Address?.City`, reported as `Customer.Address`, including inline materializer and inherited-navigation forms), conditional element access on the result (`orders?[0].Customer`), and locals initialized from a conditional indexer (`var o = orders?[0];`). Conditional method-call results such as `(order?.Customer.GetDetached())?.Address` are treated as call results, not as a continuation of the queried navigation path.
- Exact top-level `AutoInclude()` chains in the nearest real `OnModelCreating` override are recognised, including constructed generic contexts. Exact unconditional `ApplyConfiguration(new TConfiguration())` calls are also recognised when the source-visible `IEntityTypeConfiguration<T>.Configure` implementation contains direct builder chains. Configuration instances supplied through locals, fields, factories, assembly scanning, builder aliases, helper-delegated settings, inferred base-method calls, deferred calls, later unproven model mutations, and transitive auto-includes on a different entity remain unproven and can still report; keep an explicit `Include`, project, or use a reviewed suppression for those shapes.
- A query widened to `IEnumerable<T>` is analysed and fixed through the local that names it. `foreach` over that local is reported, and the fix goes where the local was given the query, not where it is consumed: `IEnumerable<Order> source = db.Orders;` becomes `IEnumerable<Order> source = db.Orders.Include(x => x.Customer);`, which still converts to the declared type because `Include` returns an `IIncludableQueryable<T, P>`. The local must be declared with an initializer that is itself queryable and never reassigned.
- **A read on a loop variable inside an expression-level conditional is missed.** `var name = order.Id > 0 ? order.Customer.Name : "";` stays quiet, as do the switch-expression, `order.Customer?.Name` and `??` spellings, while the equivalent `if`/`else` statement reports and the same read on a single materialized entity reports. It is not branch conservatism — the ternary stays quiet even when both arms read the navigation. This is a defect in the origin-flow prover rather than a deliberate limit, and it is pinned by tests so closing it is deliberate.
- Current scope is intra-procedural and local-based (methods and constructors). Out of scope (quiet, not flagged): `await foreach` over anything other than a proven EF stream, arbitrary callbacks and delegate/method-group consumers, the default-value element extractor overloads (their default can be an entity the query never produced), custom extraction lookalikes, provider-specific temporal APIs, `Find*`, and `IQueryable` parameters / repository-returned queries as roots.

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

var totals = db.Orders.ToList();
var sum = totals.Sum(o => o.Customer.Rating);                    // LC045: Customer

var found = db.Orders.ToList().First(o => o.Id == id);
Console.WriteLine(found.Customer.Name);                          // LC045: Customer

var page = db.Orders.ToList();
foreach (var o in page.Where(o => o.Status == "New").OrderBy(o => o.Id).Take(10))
    Console.WriteLine(o.Customer.Name);                          // LC045: Customer

await foreach (var o in db.Orders.AsAsyncEnumerable())
    Console.WriteLine(o.Customer.Name);                          // LC045: Customer

await foreach (var o in db.Orders) Console.WriteLine(o.Customer.Name);       // LC045: Customer
```

### Valid
```csharp
var orders = db.Orders.Include(o => o.Customer).ToList();
foreach (var o in orders) Console.WriteLine(o.Customer.Name);

var nested = db.Orders.Include(o => o.Items).ThenInclude(i => i.Product).ToList();
foreach (var order in nested)
foreach (var item in order.Items) Console.WriteLine(item.Product.Name);

// The exact context model guarantees Customer for every Order query.
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude();
}
var autoIncluded = db.Orders.ToList();
foreach (var order in autoIncluded) Console.WriteLine(order.Customer.Name);

await foreach (var o in db.Orders.Include(x => x.Customer).AsAsyncEnumerable())
    Console.WriteLine(o.Customer.Name);
await foreach (var o in stream) Console.WriteLine(o.Customer.Name); // arbitrary IAsyncEnumerable — not a proven EF stream
foreach (var o in orders.Where(o => Load(o))) Console.WriteLine(o.Customer.Name); // effectful predicate — boundary
// Exact source-visible configuration classes are also recognised.
sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
        => builder.Navigation(o => o.Customer).AutoInclude();
}
protected override void OnModelCreating(ModelBuilder modelBuilder)
    => modelBuilder.ApplyConfiguration(new OrderConfiguration());

var names = db.Orders.Select(o => o.Customer.Name).ToList();     // projection — out of scope

var list = db.Orders.ToList();
return list;                                                     // no navigation read before the escape
```
