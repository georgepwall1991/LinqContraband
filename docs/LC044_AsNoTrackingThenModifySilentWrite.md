---
layout: default
title: "Spec: LC044 - AsNoTracking query mutated then SaveChanges — silent data loss"
---

# Spec: LC044 - AsNoTracking query mutated then SaveChanges — silent data loss

## Goal
Detect the chain `AsNoTracking origin → property or nested-member mutation → SaveChanges on the same context` when no persistence-enabling tracking operation intervenes. `Update` / `UpdateRange` or `Entry(entity).State = Modified | Added` can persist an existing mutation; `Attach` / `AttachRange` are sufficient only when guaranteed before the mutation. Otherwise EF Core silently persists nothing — no exception, no log — and callers typically spend hours debugging why their update "didn't stick".

## The Problem
`AsNoTracking()` tells EF Core not to track the entity in the change tracker. A subsequent property mutation has no effect on the DbContext state, and `SaveChanges` returns `0`. Because there is no diagnostic at runtime, silent data loss is extremely hard to notice until production traffic discovers missing updates.

### Example Violation
```csharp
// Silent data loss: `SaveChanges` persists nothing.
var user = db.Users.AsNoTracking().FirstOrDefault(u => u.Id == id);
user.Name = "New Name";
db.SaveChanges();
```

The same loss occurs when a property on the materialized entity graph is changed:
```csharp
var user = db.Users.AsNoTracking().FirstOrDefault(u => u.Id == id);
user.Address.City = "London";
db.SaveChanges();
```

### Fixes
Either remove `AsNoTracking` so the entity is tracked from origin:
```csharp
var user = db.Users.FirstOrDefault(u => u.Id == id);
user.Name = "New Name";
db.SaveChanges();
```
…or explicitly mark the detached entity for persistence before saving:
```csharp
var user = db.Users.AsNoTracking().FirstOrDefault(u => u.Id == id);
user.Name = "New Name";
db.Users.Update(user);     // or db.Entry(user).State = EntityState.Modified;
db.SaveChanges();
```

`Attach` is safe only before the mutation, because EF then snapshots the original values and detects the later write:
```csharp
var user = db.Users.AsNoTracking().FirstOrDefault(u => u.Id == id);
db.Attach(user);
user.Name = "New Name";
db.SaveChanges();
```

Calling `Attach` after changing `user.Name` snapshots the changed value as the baseline `Unchanged` state, so that write is still silently lost.

## Analyzer Logic

### ID: `LC044`
### Category: `Reliability`
### Severity: `Warning`

### Algorithm
1. **Anchor**: register on every `DbContext.SaveChanges` / `SaveChangesAsync` invocation.
2. **Context symbol**: resolve the instance symbol of the SaveChanges call.
3. **Origin scan**: in the same executable root, collect local declarations whose initializer is a materializer invocation (`First`, `FirstOrDefault`, `Single`, `ToList`, async variants, …) and whose chain contains `AsNoTracking`. Collect `foreach` loops whose collection chain contains `AsNoTracking`. The chain scan honours the **last** tracking directive (each `AsTracking()`/`AsNoTracking()` overwrites `QueryTrackingBehavior`): `AsNoTracking().AsTracking()` is tracked and does **not** report, while `AsTracking().AsNoTracking()` is untracked and does.
4. **Same-context gate**: extract the DbSet-owner context symbol from the query chain and require `SymbolEqualityComparer` match against the SaveChanges context symbol.
5. **Single-assignment gate**: skip locals that are assigned more than once (ambiguous dataflow).
6. **Mutation scan**: inspect property mutations whose property receiver chain is rooted in the local and positioned between the local's declaration and the SaveChanges. Both direct writes (`entity.Prop = …`) and nested graph writes (`entity.Owned.Prop = …`) count. Unconfigured field-only receiver paths are excluded because EF Core does not conventionally persist standalone fields. Each mutation is evaluated independently so a safely reattached earlier member path cannot hide a later untracked sibling write; LC044 reports the first mutation that can still reach the save unpersisted. Plain assignments, compound assignments (`entity.Prop += …`), and increment/decrement operations (`entity.Prop++`) are all mutations of the untracked graph and are silently lost. Any receiver member explicitly marked `[NotMapped]` ends the chain so transient UI/domain state is not treated as an EF write.
7. **Block reachability**: the mutation must be in the same executable root as the SaveChanges and able to reach it. The mutation's block and the SaveChanges's block must be on the same path; sibling `if`/`else` branches and switch sections do not reach each other, while an exact explicit throw, an unhandled nested throw, or a potentially throwing call/getter/index access can transfer from a `try` mutation to a matching catch save, and every completed try/catch path enters its `finally`. Explicit terminators between the mutation and the SaveChanges break reachability, including foreach mutation branches that always return or throw before a later save.
8. **Earlier-save gate**: if another SaveChanges on the same context is guaranteed on every path from the mutation to the current save, this anchor is not the silent-write site; skip. A lexically earlier save in an optional or mutually exclusive branch, or one bypassed by a reachable branch or caught-exception transfer, does not suppress the later diagnostic.
9. **Tracking-state gate**: scan for `DbContext.Update/Attach/UpdateRange/AttachRange`, their `DbSet` counterparts, or `DbContext.Entry(entity).State = EntityState.Modified | Added` on the same context. Receiver paths are compared segment by segment: graph-traversing `Update`/`UpdateRange` (and pre-mutation `Attach`/`AttachRange`) on the queried root, the mutated nested entity, or an owning member path cover that target graph, while explicit `Entry(entity).State = Modified | Added` covers only the exact entity path passed to `Entry`; an operation on a sibling member never suppresses the diagnostic. Constant indexer arguments are part of the path and use length-prefixed null-aware encodings, so updating element `[1]` cannot suppress a mutation of `[0]`, and a `null` key cannot collide with the literal `"<null>"`; an unresolved index remains conservative. `Update` / `UpdateRange` and exact-entity explicit `Modified` / `Added` state persist an existing mutation; `Attach` / `AttachRange` suppress only when they execute before and dominate the mutation. Range calls inspect every entity argument, including compiler-created `params` arrays. A matching post-mutation persistence operation suppresses only when every mutation-to-save path executes it and the operation itself completes before any save-reaching handler; it is invalidated by a reachable exact-path `Entry(entity).State = Detached` or graph-wide `ChangeTracker.Clear()` before SaveChanges, but an invalidation confined to a mutually exclusive sibling branch does not contaminate the mutation path. One call need not dominate by itself: complementary or nested exhaustive `if`/`else` branches, guaranteed nested blocks, and mandatory `do` bodies can satisfy the proof collectively, while an optional inner or outer branch does not. Condition evaluation and every potentially throwing operation reachable from the mutation before or at a required reattach must complete before that branch can contribute proof; this includes the persistence invocation itself, nullable instance-field reads, object creation, property/indexer getters, and array access, while an operation in a mutually exclusive sibling branch is ignored. An inner handler that consumes the transfer and resumes before the reattach remains safe even when an outer handler can fall through to the save. A normal `try` path plus every local catch handler that can fall through to the save can likewise establish collective coverage, including when a pre-reattach operation transfers into a catch that guarantees the same persistence operation; catch-only persistence is accepted only when an exact terminal throw makes that catch mandatory. Span-pruned explicit-throw analysis covers statements and conditional/coalescing throw expressions while rejecting throws contained by an incomplete mutation, throws from uninvoked lambdas or local functions, and mutually exclusive sibling switch-expression arms. It retains exact and open exception channels through ordered catches, filters, rethrows, typed replacements, and unhandled nested throws, including a replacement that bypasses a later safe rethrow in the same handler. A harmless `finally` preserves proof, while exact-entity detach invalidates that entity path and tracker clear invalidates every path. Reachable `break`, `continue`, and `goto` transfers keep the diagnostic active only when their target skips the required operation; a `do` body is treated as executing once, while an ordinary `while` body remains optional. The same per-mutation control-flow proof applies inside `foreach`.
   A pre-mutation `Attach`, `Update`, or explicit state assignment contributes tracking proof only when the operation itself completes before any handler that can still reach the mutation. An optional catch-only reattach cannot cover the normal try path, and a terminal throw cannot make its attaching catch sufficient when an earlier operation or the terminal throw operand can reach another fall-through handler. A potentially-null terminal throw operand contributes an exact `NullReferenceException` channel before the declared operand type is thrown; ordered catch routing determines whether that channel reaches an alternate handler, and collective tracking remains safe when that handler also completes the matching reattach. Source-visible, third-party, and nested metadata constructor/factory failures are treated as open; only the semantically top-level construction of a core-library exception in the `System` namespace tree, after unwrapping parentheses and conversions, uses that constructed exception as exact evidence. Nested constructor operands remain open even when every branch constructs the same exception type. A user-defined exception remains open even when declared under a `System.*` namespace. Instance-field and array operand failures are routed by their exact runtime exception types, while a non-constant static-field read also models `TypeInitializationException`; compile-time constants and enum members are inlined and cannot run a type initializer. Exact exception routing honours ordered catches, so an earlier unfiltered or constant-true compatible handler intercepts the transfer before later handlers are considered. When both the normal path and every mutation-reaching handler complete a matching reattach, their collective proof is safe; a call that throws out of those paths before mutation remains unreachable, just as with an incomplete post-mutation persistence call.
   A coalescing operand can be null only when its fallback can produce null; a fallback throw expression never produces the outer throw operand, so its own exception routing is analysed separately rather than inventing a `NullReferenceException` from the outer throw.
   An unchanged local follows its initializer's nullability, while any earlier direct assignment, increment/decrement, or `ref`/`out` write in the same executable root restores the conservative potentially-null channel. Captured writes in a reachable local function or delegate invocation also count, including a local function declared later in the body, a lambda wrapped by an explicit delegate construction, a lambda selected by a conditional or switch expression, delegate aliases, conditional-access invocation, and nested invoked delegates. Delegate proof follows a lambda installed by a feasible simple or additive assignment to one or more locals within its nearest executable boundary, then invoked directly or through explicit `Invoke` before the terminal reference on a path that can still reach it, including a later loop iteration through the body or a `for` iterator when a type-safe, unchanged counter and the invocation path leave a feasible back-edge. Recursive delegate graphs are cycle-guarded. Optional installation or invocation still contributes a potentially-null path, and a locally caught throw after invocation can still reach the terminal operand; mutually exclusive inverse boolean guards are correlated across nested guards but invalidated by direct or invoked writes to the guard, while unreachable, jump-skipped, overflow-sensitive, or genuinely terminating invocation paths are excluded. A caught throw terminates the back-edge only when exact ordered catch types and filters prove that no matching in-loop handler falls through, and a `goto` terminates it only when its resolved label leaves the loop. Tuple/deconstruction, directly or transitively invoked nested writers, or written `ref` aliases invalidate the at-most-once counter proof; indexed counter reads, read-only aliases and `in` arguments, and writers in uninvoked or jump-skipped nested executables do not. Explicit and implicit transfers, ordered catches, and the possible `NullReferenceException` from a nullable throw operand remain part of reachability. A definite intervening simple or tuple replacement, an unconditional replacement performed by an invoked local function, or matched self-removal removes the stale binding only when the replacement completes. Conditional or self-preserving replacement, a nested replacement whose prior work or right-hand side can fail into a fall-through catch, self-assignment, unmatched removal, non-mutating `ref` exposure, and later additive handlers leave the writing path feasible. Null-coalescing assignment, member writes through the local, writes after the terminal reference, and writes inside an uninvoked lambda/local function do not count.
10. **Emit**: report on the property reference (`entity.Prop`) of the first matching mutation with the entity name and the property name.

## False-Positive Disciplines
- Entity from a tracked query (no `.AsNoTracking()` in the chain).
- `.AsNoTracking()` query followed only by reads, never by a property write.
- Same-context `Update` / `UpdateRange` or explicit `Modified` / `Added` state for the queried root or mutated member path when guaranteed on every mutation-to-save path, including complementary `if`/`else` or normal/catch paths. A guaranteed matching `Attach` / `AttachRange` before the mutation is also safe when the normal path and every mutation-reaching handler complete tracking and it is not invalidated by an explicit detach or tracker clear before SaveChanges. Suppression is per mutation: an optional operation, a post-mutation `Attach`, an operation on a sibling graph member, or safely persisting an earlier mutation on another path does not suppress an untracked write.
- Conditional tracking before the mutation is not treated as safe, because another path can still mutate and save the entity while it remains untracked.
- A pre-mutation tracking operation that can throw into a handler which still reaches the mutation is not treated as safe; an alternate fall-through handler is unsafe unless it also completes matching tracking, while a terminating handler is safe because the untracked path cannot reach the write or save.
- SaveChanges is on a different context instance than the query.
- Multiple reassignments to the same local (ambiguous dataflow).
- Mutation sits inside a branch that's not an ancestor of the SaveChanges's block (e.g., an `if` branch that returns or throws early, or a sibling branch the SaveChanges cannot reach).
- Entity arrives as a parameter from outside the method (v1 scope is intra-procedural; cross-method is future work).
- A different entity is mutated (symbol identity on `ILocalSymbol` prevents cross-entity confusion).
- A source-visible member in the receiver chain is marked `[NotMapped]`.
- Mutation is a collection member call (`list.Add(x)`), not an entity property assignment.

## Test Cases

### Violations
```csharp
var u = ctx.Users.AsNoTracking().FirstOrDefault();
u.Name = "x";
ctx.SaveChanges();
```

### Valid
```csharp
var u = ctx.Users.AsNoTracking().First();
ctx.Users.Update(u);
u.Name = "x";
ctx.SaveChanges();
```
