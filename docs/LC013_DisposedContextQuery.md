# LC013: Disposed Context Query (Design)

## Goal
Flag returns of deferred EF Core queries (`IQueryable<T>`, `IOrderedQueryable<T>`, `DbSet<T>`, `IAsyncEnumerable<T>`) that are built from a `DbContext` created and disposed in the same method (e.g., via `using` / `await using`). Enumerating these queries later throws `ObjectDisposedException` or causes subtle runtime failures. The rule should **not** fire when:
- The context is injected or passed in (lifetime managed elsewhere)
- The result is materialized (e.g., `ToList`, `FirstAsync`, `Count`)

## Why the compiler won’t save you
The compiler does not warn when you return an `IQueryable` built from a context that is disposed. The failure only occurs at enumeration time, often far from the original method, making it a reliability pitfall.

## Scope and Heuristics
- **Flag**: `return` expressions whose root source is a local `DbContext` declared with `using` / `await using` in the same method.
- **Return types considered deferred**:
  - `IQueryable<T>`, `IOrderedQueryable<T>`
  - `DbSet<T>`
  - `IAsyncEnumerable<T>` (async streaming still deferred on the context)
- **Do NOT flag**:
  - Materialized results (`List<T>`, arrays, scalars)
  - Contexts from parameters, fields, properties (assumed externally managed)
  - Eager execution (`ToList`, `ToArray`, `Any`, `Count`, etc.)

## Detection Strategy (Operation-based)
1) Register `OperationKind.Return`.
2) For the returned value:
   - Unwrap conversions.
   - If the resulting type is not a deferred type (above), skip.
3) Walk the expression to find the root receiver:
   - Follow `Invocation.Instance`, or first argument for extension methods.
   - Follow `MemberAccess.Instance` for property/field chains.
4) If the root is an `ILocalReferenceOperation`, inspect the local symbol:
   - For each `DeclaringSyntaxReference` of the local:
     - Flag if the declarator sits under a `LocalDeclarationStatement` with `using` or `await using`, or under a `UsingStatement`.
5) Report the diagnostic on the returned expression, naming the context variable.

## False Positive Boundaries
- **Not flagged**: injected/parameter contexts, materialized returns.
- **String includes / query shape**: irrelevant here; only the lifetime matters.
- **Nested helpers**: If a `using var ctx` is passed to another helper that returns the query, we currently miss it (acceptable for v1).

## Diagnostic Shape
- Id: `LC013`
- Severity: Warning (could be Error in stricter configs)
- Message: `"The query is built from DbContext '{0}' which is disposed before enumeration. Materialize before returning."`

## Tests (XUnit + Microsoft.CodeAnalysis.Testing)
- **Should trigger**:
  - `using var ctx = new DbContext(); return ctx.Set<User>();`
  - `using (var ctx = new DbContext()) { return ctx.Set<User>().Where(...); }`
  - `await using var ctx = new DbContext(); return ctx.Set<User>();`
- **Should not trigger**:
  - `return ctx.Set<User>();` where `ctx` is a parameter/field.
  - `using var ctx = ...; return ctx.Set<User>().ToList();` (materialized)
  - `using var ctx = ...; return ctx.Set<User>().Count();` (scalar executed)

## Implementation Notes
- Use `Microsoft.CodeAnalysis.CSharp.Syntax` to inspect `using`/`await using` on declarations.
- Keep detection purely operation-based; avoid semantic symbol lookups beyond type checks and locals.
- Keep performance in check: only analyze `Return` operations; no solution-wide symbol scans.

## Status
- Not yet implemented in codebase. This document captures the intended behavior and test plan for a future LC013 addition.
