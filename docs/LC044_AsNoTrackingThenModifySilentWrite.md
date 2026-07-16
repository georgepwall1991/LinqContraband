---
layout: default
title: "Spec: LC044 - AsNoTracking query mutated then SaveChanges — silent data loss"
---

# Spec: LC044 - AsNoTracking query mutated then SaveChanges — silent data loss

## Goal
Detect the chain `AsNoTracking origin → property or nested-member mutation → SaveChanges on the same context` when no re-attach (`Update` / `Attach` / `Entry(entity).State = Modified | Added`) intervenes. In this pattern EF Core silently persists nothing — no exception, no log — and callers typically spend hours debugging why their update "didn't stick".

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
…or re-attach before saving:
```csharp
var user = db.Users.AsNoTracking().FirstOrDefault(u => u.Id == id);
user.Name = "New Name";
db.Users.Update(user);     // or db.Attach(user); or db.Entry(user).State = EntityState.Modified;
db.SaveChanges();
```

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
6. **Mutation scan**: inspect property mutations whose property/field receiver chain is rooted in the local and positioned between the local's declaration and the SaveChanges. Both direct writes (`entity.Prop = …`) and nested graph writes (`entity.Owned.Prop = …`) count. Each mutation is evaluated independently so a safely reattached earlier member path cannot hide a later untracked sibling write; LC044 reports the first mutation that can still reach the save unpersisted. Plain assignments, compound assignments (`entity.Prop += …`), and increment/decrement operations (`entity.Prop++`) are all mutations of the untracked graph and are silently lost. Any receiver member explicitly marked `[NotMapped]` ends the chain so transient UI/domain state is not treated as an EF write.
7. **Block reachability**: the mutation must be in the same executable root as the SaveChanges and reachable from it. The mutation's block and the SaveChanges's block must be on the same branch (one is the same block, an ancestor, or a descendant of the other); blocks that are siblings under different `if`/`else` branches or `switch` sections do not reach each other. Explicit `return`/`throw` terminators between the mutation and the SaveChanges break reachability.
8. **Earlier-save gate**: if another SaveChanges on the same context is guaranteed on every path from the mutation to the current save, this anchor is not the silent-write site; skip. A lexically earlier save in an optional or mutually exclusive branch, or one bypassed by a reachable branch or caught-exception transfer, does not suppress the later diagnostic.
9. **Re-attach gate**: scan for `DbContext.Update/Attach/UpdateRange/AttachRange`, their `DbSet` counterparts, or `DbContext.Entry(entity).State = EntityState.Modified | Added` — on the same context, with the entity as the argument. Receiver paths are compared segment by segment: re-attaching the queried root, the mutated nested entity, or an owning member path suppresses the diagnostic, while re-attaching a sibling member does not. A matching re-attach after the mutation and before SaveChanges suppresses only when every mutation-to-save path executes it and it is not invalidated by a reachable matching `Entry(entity).State = Detached` or `ChangeTracker.Clear()` before SaveChanges. Reachable `break`, `continue`, and `goto` transfers keep the diagnostic active only when their target skips the re-attach; a branch to a re-attach after the loop or at a label remains safe. An explicit throw keeps the diagnostic active only when it can bypass a re-attach inside the exited `try`, a compatible non-constant-false local catch handles it, and the handler can reach SaveChanges without reattaching. A post-try re-attach, non-matching or constant-false catch, mutually exclusive mutation/throw branches, or handler that returns or jumps past the save stays quiet. A matching re-attach before the mutation also suppresses only when it dominates the mutation path and remains valid through SaveChanges. The same per-mutation control-flow proof applies inside `foreach`; an optional branch re-attach is not enough.
10. **Emit**: report on the property reference (`entity.Prop`) of the first matching mutation with the entity name and the property name.

## False-Positive Disciplines
- Entity from a tracked query (no `.AsNoTracking()` in the chain).
- `.AsNoTracking()` query followed only by reads, never by a property write.
- Same-context re-attach of the queried root or the mutated member path that is guaranteed on every mutation-to-save path, or a guaranteed matching re-attach before the mutation that is not invalidated by a matching explicit detach or tracker clear before SaveChanges. Suppression is per mutation: an optional re-attach, re-attaching a sibling graph member, or safely persisting an earlier mutation on another path does not suppress an untracked write.
- Conditional re-attach before the mutation is not treated as safe, because another path can still mutate and save the entity while it remains untracked.
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
