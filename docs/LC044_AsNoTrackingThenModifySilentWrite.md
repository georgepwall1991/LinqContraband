# Spec: LC044 - AsNoTracking query mutated then SaveChanges — silent data loss

## Goal
Detect the chain `AsNoTracking origin → property mutation → SaveChanges on the same context` when no re-attach (`Update` / `Attach` / `Entry(entity).State = Modified | Added`) intervenes. In this pattern EF Core silently persists nothing — no exception, no log — and callers typically spend hours debugging why their update "didn't stick".

## The Problem
`AsNoTracking()` tells EF Core not to track the entity in the change tracker. A subsequent property mutation has no effect on the DbContext state, and `SaveChanges` returns `0`. Because there is no diagnostic at runtime, silent data loss is extremely hard to notice until production traffic discovers missing updates.

### Example Violation
```csharp
// Silent data loss: `SaveChanges` persists nothing.
var user = db.Users.AsNoTracking().FirstOrDefault(u => u.Id == id);
user.Name = "New Name";
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
3. **Origin scan**: in the same executable root, collect local declarations whose initializer is a materializer invocation (`First`, `FirstOrDefault`, `Single`, `ToList`, async variants, …) and whose chain contains `AsNoTracking`. Collect `foreach` loops whose collection chain contains `AsNoTracking`.
4. **Same-context gate**: extract the DbSet-owner context symbol from the query chain and require `SymbolEqualityComparer` match against the SaveChanges context symbol.
5. **Single-assignment gate**: skip locals that are assigned more than once (ambiguous dataflow).
6. **Mutation scan**: find the first `ISimpleAssignmentOperation` whose target is an `IPropertyReferenceOperation` instance-referencing the local, positioned between the local's declaration and the SaveChanges.
7. **Block reachability**: the mutation's enclosing `IBlockOperation` must be an ancestor of (or equal to) the SaveChanges's enclosing block — otherwise the mutation lives in a branch the SaveChanges can't reach in lexical order.
8. **Earlier-save gate**: if another SaveChanges on the same context already ran between the mutation and the current one, this anchor is not the silent-write site; skip.
9. **Re-attach gate**: between the mutation and the SaveChanges, scan for `DbContext.Update/Attach/UpdateRange/AttachRange`, their `DbSet` counterparts, or `DbContext.Entry(entity).State = EntityState.Modified | Added` — on the same context, with the entity as the argument. If any such re-attach is found, suppress the diagnostic.
10. **Emit**: report on the property reference (`entity.Prop`) of the first matching mutation with the entity name and the property name.

## False-Positive Disciplines
- Entity from a tracked query (no `.AsNoTracking()` in the chain).
- `.AsNoTracking()` query followed only by reads, never by a property write.
- Re-attach of any form present between the mutation and SaveChanges.
- SaveChanges is on a different context instance than the query.
- Multiple reassignments to the same local (ambiguous dataflow).
- Mutation sits inside a branch that's not an ancestor of the SaveChanges's block (e.g., an `if` branch that returns early).
- Entity arrives as a parameter from outside the method (v1 scope is intra-procedural; cross-method is future work).
- A different entity is mutated (symbol identity on `ILocalSymbol` prevents cross-entity confusion).
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
u.Name = "x";
ctx.Users.Update(u);
ctx.SaveChanges();
```
