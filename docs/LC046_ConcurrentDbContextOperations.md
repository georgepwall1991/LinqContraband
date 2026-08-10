---
layout: default
title: "Spec: LC046 - Concurrent DbContext Operations"
---

# Spec: LC046 - Concurrent DbContext Operations

## Goal

Detect overlapping Entity Framework Core operations that are proven to use the same `DbContext` instance.

## The Problem

Entity Framework Core does not support multiple parallel operations on one context. Starting another query or save
before the previous task completes can throw `InvalidOperationException`; when the overlap escapes EF Core's guard,
the context's state is undefined.

### Example Violation

```csharp
var users = db.Users.ToListAsync(cancellationToken);
var roles = db.Roles.ToListAsync(cancellationToken); // LC046
await Task.WhenAll(users, roles);
```

The same risk appears when one operation is a query and the other is a save, bulk command, `FindAsync`, `LoadAsync`,
or relational raw command.

## Safer Shapes

Await operations sequentially when they belong to one unit of work:

```csharp
var users = await db.Users.ToListAsync(cancellationToken);
var roles = await db.Roles.ToListAsync(cancellationToken);
```

When the work is genuinely independent and parallelism is intentional, create a separate context for each operation:

```csharp
var usersTask = LoadUsersAsync(factory, cancellationToken);
var rolesTask = LoadRolesAsync(factory, cancellationToken);
await Task.WhenAll(usersTask, rolesTask);
```

Each helper must create and dispose its own context.

## Analyzer Logic

### ID: `LC046`
### Category: `Safety`
### Severity: `Warning`

For direct overlap, LC046 reports the second proven overlapping EF Core invocation and points back to the first
operation as an additional location. It recognises async query terminals, including `ContainsAsync`, `ElementAtAsync`, and
`ElementAtOrDefaultAsync`, plus `FindAsync`, `SaveChangesAsync`, `LoadAsync`, `ExecuteUpdateAsync`,
`ExecuteDeleteAsync`, and relational `ExecuteSql*Async` commands.

For direct overlap, the analyzer also follows a parameterless source-visible local function when its body consists of
one direct return of a recognised EF Core async invocation over a context captured from outside the helper. The
diagnostic is reported on the repeated helper call, with the earlier call as an additional location. Parameterized
helpers, helper chains, branch or multi-operation bodies, and contexts constructed inside the helper remain outside
this deliberately narrow interprocedural proof.

The analyzer follows stable locals, parameters, readonly fields, source-visible auto-properties, `DbSet` members,
`DbContext.Set<TEntity>()`, and transparent LINQ or EF query chains. It also reports
`Task.WhenAll(items.Select(...))` when the selector captures one outer context and the source can contain multiple
items. Instance context members are matched by both the member and its proven receiver, so the same member on two
different holder objects is not conflated.

A separate loop pass reports the loop-body invocation itself when a `foreach` iterates an inline array initializer with
at least two elements and the loop body's only statement either discards the EF Core async invocation or passes it
directly to `Add` on a single-assignment local whose proven runtime construction is a framework `List<T>`, including
when the local is declared through `ICollection<T>` or `IList<T>`. The list must be constructed with `new` or, when the
local's target type is itself `List<T>`, a collection expression
before the loop, either at declaration or by one later simple assignment. The accepted accumulator construction is
an empty parameterless `new` or empty collection expression; an interface parameter or other unproven collection
implementation remains outside the proof. The task-list branch additionally requires
a synchronous, non-deconstructing loop over a direct inline array with at least two compile-time-constant elements and
an identity iteration-variable conversion. It does not report that branch for an unknown, empty, or singleton source,
an asynchronous or deconstructing loop, a source whose setup can throw before repetition, a user-defined source or
iteration-variable conversion, a multi-statement or conditionally exited loop body, a context that can change between
iterations, an awaited result, a throwing or unstable list receiver, a potentially throwing explicit cast or
user-defined conversion around the task, an evaluated or non-empty accumulator construction, a task-producing call that may consume the accumulator directly, through one
or more alias assignments, or through an invoked captured local before starting the next operation, a potentially
throwing query-receiver evaluation, explicit argument conversion, expanded `params` element, or other invocation
argument that can prevent a later EF call from starting, an invalid query-construction argument including a null
required sequence, callable, or nullable instance method-group receiver, string, or formattable-string parameter, a required terminal callable
argument, a null or blank required raw-SQL argument, a null required `FindAsync` key array, an unguarded,
empty `FindAsync` key array or an array containing an unproven/null key, a null raw-SQL parameter collection, a possibly-empty raw-SQL interpolation,
a definitely-cancelled token, an unproven-non-blank `DbContext.Set<TEntity>(name)` argument, an unguarded,
null-suppressed, or nullable-oblivious context parameter, a nullable local query alias, a nullable or
constructor-invalidated stored query member, or a static member whose type initialization is not proven safe, loop source setup that
references the accumulator between body executions, any executable use or retained closure of the accumulator between
its construction and the loop's `Add` receiver, or a custom
`Add`-shaped API. Safe explicit identity and reference upcasts around the task retain the diagnostic.
Null-conditional `Add` remains diagnostic when the same construction proof establishes that the local receiver cannot
be null. A local or anonymous function that captures the accumulator affects the proof only when its reachable direct
or delegate invocation can run before the loop or its binding escapes local control; a locally bound invocation that
occurs only after the loop does not suppress the diagnostic. A nullable context parameter retains the diagnostic only after
nullable flow analysis proves a preceding guard; null forgiveness alone is not treated as runtime proof, while redundant
suppression after a proven `is null` or built-in equality null-exit guard retains that proof only when no intervening
parameter write invalidates it. Overloaded equality is not accepted as null proof. The known
metadata-backed EF Core `DbContext.Database` property retains relational-command diagnostics without requiring source
declarations, while required terminal non-blank SQL, raw-SQL parameter collections, non-empty key arrays containing
only proven non-null keys, and cancellation tokens must
permit a task to start before the overlap is reported. `CancellationToken.None` is a readonly static of a
core-library struct, so reading it cannot throw and it never suppresses the diagnostic. A definitely-cancelled token
suppresses, as does any token expression whose evaluation cannot be proven non-throwing. Outside loops, an operation is quiet when a required argument is *provably* invalid — a literal null or blank
SQL string or set name, a null key array or parameter collection, or a definitely-cancelled token — because the
call then faults before starting any work and cannot overlap anything. The same applies to query construction
that provably faults on its own arguments. An argument the analyzer cannot evaluate, such as one supplied by a
parameter or field, does **not** suppress the diagnostic. Inside a loop the burden is the opposite and stricter:
validity must be positively proven, because the loop gate has to establish that the operation starts on every
iteration.
A named `DbContext.Set<TEntity>(name)` root must have a provably non-blank name whether or not it appears in a loop,
because EF Core rejects a null or whitespace name before any query is constructed. Proof covers constant strings, interpolated
strings with non-whitespace literal text, and single-assignment locals resolving to either; a name that cannot be
proven non-blank stays quiet, which is a deliberate conservative false negative.

To preserve precision, LC046 stays quiet for sequential awaits, separate contexts, branch-exclusive operations,
unproven reassigned or escaped task/context state, repository-produced `IQueryable` values, computed context or set properties,
custom lookalike APIs, query construction, `AsAsyncEnumerable()` alone, per-item context factories, and selector
fan-out over statically empty or singleton sources, including fixed-size arrays. LC036 continues to own `Task.Run`,
`Parallel`, `Thread`, thread-pool, and timer capture diagnostics.

An await or task escape suppresses the diagnostic only when it is guaranteed to execute before the later EF Core
operation. A conditional await or an exception path that can bypass an await still reports because another reaching
path can leave the first operation active, including when argument evaluation throws after the EF task starts but
before an immediate, task-local, or `Task.WhenAll` wrapper reaches the await. Explicit throws reach a handler only
when its type and filter permit it, and a compatible nested catch can intercept that transfer. An earlier
nonconstant filtered catch that can propagate the exception prevents a later catch from being treated as a definite
interceptor, and a nested catch or `finally` can replace the original transfer before an outer continuing catch.
Exact single-assignment replacement locals retain their constructed exception type, and an
always-throwing `finally` prevents the original exception from reaching a handler that cannot accept any of its
replacement exceptions. Each
independently drained and restarted overlap group receives its own diagnostic. Awaiting a stored `Task.WhenAll`, a
stored single-input `Task.WhenAny`, a direct one-element array-backed or span-backed collection expression whose
direct element is the tracked task, or a single-assignment `Task[]` whose only direct element is the tracked task in
an awaited `Task.WhenAny` ends a
proven task lifetime. The singleton-array combinator may be awaited directly, through an
exact framework task wrapper, or through its single-assignment stored result, and the array may be initialized at its
declaration with array-initializer or collection-expression syntax or assigned once later; other read-only `WhenAll`
or `WhenAny` uses of the same stable array do not invalidate a later proven completion. Array initializer and
collection-expression elements both participate in non-null `WhenAll` proof, and a known null collection-expression
element retains its exact `ArgumentException` route. `Task.WhenAny` can fail while allocating its result before its
await completes, including a direct span-backed collection expression or when that result is first stored in a local,
but that allocation path is considered only when the combinator invocation itself occurs inside the continuing
handler's `try`. An array-backed
`IEnumerable<Task>` collection expression is likewise an `OutOfMemoryException` allocation path. An allocation
failure (`OutOfMemoryException`, or `OverflowException` from a runtime-sized or oversized constant dimension or a
potentially overflowing checked built-in conversion), an `InvalidOperationException` from a nullable length conversion, or a user-defined
conversion or operator that can reach a continuing handler without being intercepted by an
earlier compatible non-propagating or terminal nested catch, a user-defined task wrapper, or an exact
`Task.FromResult`-wrapped task used directly or through a local, multi-element, mutated including through non-lexical control flow,
aliased, captured, unawaited, or reassigned shape remains conservative and does not prove completion. Unknown task
consumers, including consumers reached through exact `Task.FromResult`, direct task-returning helpers in a task-array
element, and consumers inside unrelated array bounds, non-task array initializers, or nested task-array element
expressions, retain the normal escape boundary. That boundary occurs only after argument evaluation, so a
metadata `Task.FromResult` allocation failure before the consumer receives the task can still leave it active, but only
`OutOfMemoryException` is routed from that framework wrapper. Branch-local escape points
combine with ordinary awaits when every path ends or transfers ownership of the task; a wrapped-task assignment must
definitely reach its consumer to transfer ownership. A branch that exits through a nested `try` return is not treated
as terminal when evaluating the opposite branch if a prefix statement, the return expression, or its `finally` can
throw to an outer continuing handler, or if one of its catch paths can fall through; event assignments are potential
throwing prefixes because a custom accessor can fail. Prefixes whose faults cannot reach the later operation, an
absent or provably empty `finally`, and catch paths that all transfer out can preserve that terminal proof. A continuing catch remains a bypass even when the
completed await appears inside a nested block, and an explicit opposite-branch throw remains reaching when either its
operand can throw into, or its declared exception can reach, a compatible outer continuing catch. User-defined
task-sequence conversions are not treated as the original stable array, and custom replacement
exception constructors remain potential exception paths before a later direct or exact throw; metadata framework
exception constructors remain precise non-throwing replacement construction.
Before a singleton `Task.WhenAny` await, a custom event accessor assignment, possibly-null field-like event receiver,
user-defined unary operator, or possibly-null instance-field receiver can likewise throw into a compatible continuing
handler, so it does not prove completion. Field-like event accessors with known receivers, null-conditional field
access, and known non-null field receivers do not create that path, and a possible field/event-receiver fault reaches
only a compatible `NullReferenceException` handler. Explicit reference or unboxing casts, checked arithmetic and
increments, and integral division/remainder are likewise evaluated through their exact
`InvalidCastException`, `NullReferenceException`, `OverflowException`, or `DivideByZeroException` routes; harmless
implicit reference and widening numeric conversions do not block terminal-branch proof. Parentheses, null-forgiveness,
and non-user-defined base casts around a direct singleton span collection element still match the tracked task. Runtime
`byte`, `ushort`, and `char` array dimensions do not have an `OverflowException` route.
Calling parameterless
`Wait()` or `GetAwaiter().GetResult()` directly or after the framework `ValueTask.AsTask()` wrapper also ends a proven
task lifetime; a timed wait does not. Selector analysis inspects only code executed by the selector itself, not
uninvoked nested lambdas or local functions. Explicitly discarding an EF task or a local that stores it does not end
its active lifetime.

## Why There Is No Code Fix

Sequential execution and separate contexts change performance, lifetime, transaction, tracking, and consistency
semantics. Choosing between them requires application intent, so LC046 is diagnostic-only.
