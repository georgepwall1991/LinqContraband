# LC045 — interprocedural scope: design notes

> **Status:** both local-function slices described here are implemented — the closure form in
> 5.7.47 and the entity-taking form in 5.7.48. What remains is genuinely cross-method:
> `IQueryable` parameters and repository-returned queries as roots, where the callee is not
> declared in the analysed method and its body may not be available at all. The constraints and
> oracles below still apply.

LC045 is intra-procedural by design. Reads reached only through a callee are quiet:

```csharp
var orders = db.Orders.ToList();
void Print() { foreach (var o in orders) Console.WriteLine(o.Customer.Name); }   // quiet
Print();

void Show(Order o) => Console.WriteLine(o.Customer.Name);                        // quiet
foreach (var o in orders) Show(o);
```

This is the largest remaining false-negative family. These notes record what closing it would
require, so the work starts from analysis rather than from discovery. **Nothing here is
implemented.**

## Why it is quiet today

`DiscoverLocalFunctionCaptures` walks each local function declared in the analysed root. Any
reference to the tracked collection sets `LocalFunctionCapture.EscapesRoot`, and at the invocation
`CollectCallEscapeEvents` turns that into an `EscapeRoot` event. An escape silences later reads,
which is correct while the callee's behaviour is unknown: it may load the navigation itself.

## The narrowest defensible slice

A local function declared in the same method, invoked exactly once, whose body's only use of the
collection is reading navigations on its elements. Entity-taking callees (`Show(Order o)`) are a
harder case and should stay out: their body may call `db.Entry(o).Reference(...).Load()`, which
LC045 has recognised since 5.7.36, so a naive lift would report code that is already correct.

## Two structural constraints, both discovered rather than assumed

**1. Attribution can avoid the nested control-flow graph.** A local function body is a separate
`ControlFlowGraph`, reachable only through `ControlFlowGraph.GetLocalFunctionControlFlowGraph`, so
the enclosing analysis cannot see its blocks. It does not need to. A `FlowEvent` carries the syntax
used for block mapping, while the `NavigationAccess` inside the candidate carries the syntax used
for the diagnostic location. Emitting the access with the **invocation** syntax and the **read**
syntax respectively puts the proof in the outer graph — where the collection's state is already
known — while the diagnostic still lands on the read itself.

**2. Reporting stays local only because of how LC045 registers.** LC045 uses
`RegisterOperationBlockAction` rather than an operation action precisely so that a report outside
the triggering operation is still classified as a local diagnostic; an operation-scoped report
outside its span becomes compilation-level, which suppresses live IDE analysis and makes the code
fix unreliable. A local function's body sits inside the enclosing method's operation blocks, so
reporting there keeps that property. Any redesign that moves LC045 to an operation action would
break this case first.

## The open question, which is the real work

The candidate's origin. A read inside the local function is on the iteration variable of a `foreach`
declared *inside* that function, which is not an origin the enclosing analysis tracks. The proof
checks `OriginBound`, `IsActive` and `OriginUnknown` for the candidate's origin, so the access has to
be attributed to an origin the outer graph binds — most plausibly the collection root, with the read
path carried on the event.

Getting that wrong does not fail loudly. It produces false positives, in the part of the analyzer
whose entire value is precision. That is why this is written down rather than attempted opportunistically.

## Conditions any implementation should carry

- The local function is invoked exactly once; more than one call site makes the read position
  ambiguous, so the escape should stand.
- The body's only use of the collection and its elements is reading navigations. Any call taking
  either, any assignment out, or any explicit-load call means the escape should stand.
- Recursive local functions, and local functions capturing a collection that escapes elsewhere in
  the method, keep today's behaviour.

## How to validate it

The oracles already exist:

- the ten real Entity Framework Core projects recorded in `analyzer-health.md`, which must stay at
  zero diagnostics, with the self-contained canary still firing;
- the nine real-EF shapes recorded alongside them, which must keep their current verdicts;
- the fixer-coverage contract, which any newly reportable shape must join in the same change.

## The implementation shape, from reading the code

The pattern to mirror already exists: `CollectNavigationCollectionCallbackEvents` binds a callback
parameter to an origin (`originsByParameter[...] = elementOrigin`) and then walks the callback body
calling `CollectNavigationEvent` for each read. Its own comment records why that works — the lambda
body has no block in this graph, so the events map to the block holding the invocation.

A local function needs the same three moves:

- **Origin.** `CreateOrigin(local, ..., isIteration: true, bindingPosition: <invocation position>)`
  for the `foreach` variable declared inside the callee, or `CreateParameterOrigin` for the
  entity-taking form.
- **Event position.** `CollectNavigationEvent` currently uses the read's own syntax for both the
  block mapping and the diagnostic location. It needs to take them separately: the **invocation**
  syntax for the event, the **read** syntax for the `NavigationAccess`.
- **Escape suppression.** `DiscoverLocalFunctionCaptures` must stop setting `EscapesRoot` for the
  lifted case, or the escape it emits at the invocation will silence the reads that were just added.

Each of those is small. The risk is not in any one of them; it is that they interact with the
existing escape and satisfy events, and a mistake shows up as a false positive rather than as a
failing build.

## The specification is already written

`MissingIncludeInterproceduralSpecTests` pins these shapes as current behaviour. `TestFutureGap_`
cases are the ones an implementation should make report — flipping them is the point.
`TestDeliberate_` cases must stay quiet whatever happens: a callee that explicitly loads, a callee
invoked twice, a callee invoked after an escape, a callee over an already-included query, and a
delegate variable. Those are the boundary, and they are why this is a feature rather than a fix.
