# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed
- `LC044` now follows nested property- and field-receiver chains back to an entity materialized by `AsNoTracking()`, so silently lost graph mutations such as `user.Address.City = value` report before same-context `SaveChanges()` just like direct entity-property writes, while source-visible `[NotMapped]` members remain excluded.

## [5.6.46] - 2026-07-16

### Fixed
- `LC034` no longer offers the `ExecuteSql`/`ExecuteSqlAsync` rewrite when an interpolated hole occupies a structural SQL position, lies inside a bracketed, double-quoted, or backtick-delimited identifier or PostgreSQL dollar-quoted literal, follows a provider-style backslash-escaped quote, is formatted or aligned, is adjacent to another hole, uses `char`, a custom, generic, enum, or framework-type lookalike value, or belongs to provider-commented, batched, or multi-statement SQL, including separator-free non-DML statements. Quote tracking now ignores apostrophes inside delimited identifiers, complete direct scalar `INSERT ... VALUES (...)` rows (including multiple rows) are recognised without treating table, column, or SQL-expression holes as values, and FixAll handles mixed synchronous/asynchronous calls; automatic fixes remain limited to proven core-library scalar values in unambiguous `UPDATE`/`DELETE`/`INSERT` value positions while the security diagnostic stays available for manual allow-listing or redesign.

## [5.6.45] - 2026-07-16

### Fixed
- `LC045` now honours exact top-level `OnModelCreating` configuration that marks the queried entity's navigation with EF Core `AutoInclude()`, including constructed generic contexts, while keeping `IgnoreAutoIncludes()`, fluent, explicit, conditional, or runtime-valued disablement, early-exit or deferred configuration, later unproven model-configuration boundaries, hidden override slots, shadowed methods, and different context/navigation paths diagnostic.

## [5.6.44] - 2026-07-14

### Fixed
- `LC045` now resolves exact static/named Queryable and EF query sources, preserves `AsQueryable`, `IgnoreAutoIncludes`, and `FromSql*` paths, detects supported `ToHashSet*` and query-root `ElementAt*` overloads, and analyses direct property subpatterns plus exact inline materialized-collection callbacks while their source generation remains active. Framework-namespace lookalikes, effectful callback chains, entity-returning projections, multi-source operators, temporal APIs, `Find*`, async streams, and repository-query roots remain conservative boundaries.
- `LC045` now follows materialized entities through intra-procedural control flow, preserving missing-`Include` diagnostics that occur before a later escape or reassignment and allowing navigation writes to satisfy later reads only for the same entity origin when the write occurs on every path to the read.
- `LC045` now follows synchronous `foreach` over inline collection materializers and proven DbSet/IQueryable roots, carries nested collection prefixes such as `Items.Product`, and recognises exact framework element extraction from materialized collections. Its fixer now anchors the exact query source for both materializer and direct-loop diagnostics while preserving widened-`IEnumerable` diagnostic-only behaviour.

## [5.6.43] - 2026-07-08

### Fixed
- `LC010` now reports `SaveChanges` and `SaveChangesAsync` calls hidden behind local delegate variables, aliases, compound or self-combining delegate subscriptions, conditional delegate initializers or invocation, delegate call chains, setup helpers including nested live helper calls and wrapper setup helpers, local invoker callback helpers, loop-carried assignments, or method groups when the delegate is invoked inside a loop, including loop-variant branch-exiting or opposite-branch delegate paths, assignments after invocation inside loop-called wrapper delegates, local functions, or callback helpers, and conditionally assigned delegates that can carry a loop-local context into later iterations, while preserving fresh loop-local contexts, fresh contexts passed directly or through wrapper delegate parameters unless those parameters are reassigned on a path that can reach the save or forwarding call, delegate removals without clearing duplicate subscriptions, branch-exclusive or branch-exiting delegate paths with stable guards, branch-exclusive conditional initializer arms with stable guards, negated-guard delegate paths, called-helper delegate overwrites, return-exiting retries, retry-only loops, switch-local retry breaks, and same-path reassignment boundaries.

## [5.6.42] - 2026-07-08

### Fixed
- `LC030` now resolves concrete singleton implementations returned directly from `AddSingleton<TService>(..., provider => new Implementation(...))` factories, tightens DI-call recognition to real `IServiceCollection` extension methods, and reports multiple stored `DbContext` diagnostics in deterministic source order.

## [5.6.41] - 2026-07-08

### Fixed
- `LC006` now fixes static `EntityFrameworkQueryableExtensions.Include(...)` chains by inserting `AsSplitQuery()` on the real query source argument instead of rewriting the extension type name and producing invalid code.

## [5.6.40] - 2026-07-08

### Fixed
- `LC045` now treats `DbContext.Set<TEntity>()` as a query root when detecting missing includes, including hoisted query locals and entity types without a matching `DbSet` property, and its fixer inserts `.Include(...)` before materialization on those sources.

## [5.6.39] - 2026-07-05

### Fixed
- `LC009` now treats nested member-state writes rooted in a materialized entity, such as `user.Profile.DisplayName = name`, as write paths, so it no longer suggests `AsNoTracking()` when a helper later commits that tracked graph mutation.
- `LC017` now withholds its anonymous-projection fixer when downstream usage includes cast, interface, or conversion-based entity property access such as `((IHasName)e).Name` or `((IHasName)e)?.Name`, preventing partial projections that leave code depending on the original entity shape.
- `LC041` now offers its scalar-projection fixer for non-key chained materializers such as `users.Where(x => x.IsActive).First()` and withholds the fixer for hoisted terminal predicates such as `users.First(active)`, preventing non-compiling rewrites that leave an entity predicate on a scalar projection.
- `LC030` no longer reports computed `DbContext` properties on proven long-lived types when the getter directly creates a fresh context through `IDbContextFactory<TContext>.CreateDbContext()` or `new TContext()`, while stored auto-properties, initialized get-only properties, and root-service-provider lookups still report.
- `LC011` now treats `ApplyConfigurationsFromAssembly(System.Reflection.Assembly.GetExecutingAssembly())`, `global::System.Reflection.Assembly.GetExecutingAssembly()`, `using Assembly = System.Reflection.Assembly`, and local or inherited immutable member aliases of that current assembly like `typeof(LocalType).Assembly`, so visible `IEntityTypeConfiguration<TEntity>` key configuration no longer produces missing-key false positives while shadowed, non-System aliased, or mutable `Assembly` identifiers stay conservative.
- `LC039` now treats different switch-expression arms as mutually exclusive save paths, so repeated `SaveChanges()` calls in separate arms no longer report as if both could run in one invocation.
- `LC013` now treats arbitrary project extension methods as query-origin boundaries, so helpers that materialize and return `IQueryable<T>` no longer receive disposed-context warnings while known LINQ/EF deferred query chains still report.
- `LC030` now reports static `DbContext` fields and properties on proven long-lived types such as hosted/background services, while still requiring long-lived-type evidence before flagging static storage.
- `LC001` now reports local helper calls inside nested query lambdas when the helper depends on an outer query range variable, and it withholds the `AsEnumerable()` fixer for nested correlated subqueries where converting only the inner query boundary would be misleading.
- `LC018` no longer offers its safe-API fixer when an interpolation hole is in a SQL identifier position such as `FROM {tableName}`, preventing rewrites that would parameterize SQL structure instead of preserving a manually reviewed raw-SQL fragment.
- `LC006` now treats reference-only prefixes before collection includes as row-preserving, so sibling collections such as `Address.Orders` and `Profile.Tags` report as a cartesian-explosion risk instead of being split into separate parent groups.
- `LC007` now reports EF query execution used as the source of a nested `foreach` when that inner loop is inside an outer loop, so per-outer-iteration materialization no longer slips past the nearest-loop check.
- `LC040` now treats `DbContext.Set<TEntity>()` as tracked query evidence when comparing mixed tracking modes, so methods that combine tracked `Set<TEntity>()` materialization with no-tracking queries on the same context report consistently.
- `LC031` now treats `ToLookup()` as a collection materializer, so unbounded EF queries that fully group rows client-side are reported like `ToList()`, `ToArray()`, `ToDictionary()`, and `ToHashSet()`.
- `LC024` now analyzes `Queryable.GroupBy(..., (key, group) => ...)` result selectors, so non-translatable grouped projections such as `group.ToList()` report even when they are written without a separate `.Select(...)` call.
- `LC025` now treats EF Core `AsNoTrackingWithIdentityResolution()` as a no-tracking query source, so entities materialized with identity resolution still report when passed back to `Update`/`Remove` or explicit modified/deleted `Entry.State` writes, and it withholds the fixer when a branch-conditional no-tracking reassignment would only remove one of multiple no-tracking origins.
- `LC010` now treats a `DbContext` declared inside the loop body as a fresh per-iteration unit of work, so those saves stay quiet instead of reporting an N+1 write or offering a move fix that would reference the context out of scope.
- `LC036` now reports `DbContext` captures in `Parallel.For(...)`, `Parallel.Invoke(...)`, and delegate-wrapped callbacks, matching the existing threading guards while keeping callbacks that create their own context quiet.
- `LC007` now reports terminal execution after a deferred `AsEnumerable()` boundary when the upstream query is provably EF-backed, while keeping LINQ-to-Objects `AsEnumerable()` aggregates quiet.
- `LC044` no longer reports AsNoTracking silent-write diagnostics when the entity is re-attached on a guaranteed path before the mutation; conditional pre-attach branches, explicit detach, and `ChangeTracker.Clear()` still report because the write can still reach `SaveChanges()` untracked.
- `LC045` no longer offers its `.Include(...)` fixer when the materialized source is statically typed as `IEnumerable<T>` rather than `IQueryable<T>`, so widened query aliases still report the missing include without producing non-compiling `source.Include(...)` output.
- `LC015` now still reports the missing upstream ordering for `Skip(...)` when the only downstream sort is misplaced and further pagination follows, including through simple query and sorted-query aliases, so `Skip(...).OrderBy(...).Take(...)` surfaces both the unordered page boundary and the misplaced sort.
- `LC016` now expands expression-bodied query methods and local functions before extracting `DateTime.Now`/`UtcNow` to a local, so the code action no longer appears to succeed while leaving arrow-bodied queries unchanged; static query lambdas stay diagnostic-only because extracting a local would create an invalid capture.
- `LC035` no longer reports bulk `ExecuteDelete*`/`ExecuteUpdate*` receivers when every ternary or switch-expression arm is filtered, or when a local query is definitely assigned through filtered `if`/`else` branches before the bulk call; any unfiltered arm still reports.
- `LC017` now includes supported null-conditional and indexed entity-property access shapes when its fixer builds an anonymous projection, so mixed downstream usage such as `e.Id` plus `e?.Name` does not leave post-fix code referencing an omitted property.
- `LC027` now uses visible Fluent `HasKey(...)` metadata when its fixer inserts a foreign-key property, so non-conventional principal keys such as `Customer.Code` produce the correct FK type instead of falling back to `int`.
- `LC023` still reports self-referential primary-key predicates such as `x.Id == x.OtherId`, but no longer offers a `Find(...)` fixer that would lift the lambda parameter outside the predicate and generate non-compiling code.

## [5.6.38] - 2026-07-04

### Fixed
- `LC012` now offers its `RemoveRange(query)` to `ExecuteDelete()` fixer when a later `SaveChanges()` is provably irrelevant because it is in a mutually exclusive branch or belongs to a different freshly-created context instance, while still withholding the fixer if the query source belongs to that later-save context, flows through an arbitrary helper, or combines multiple query sources.

## [5.6.37] - 2026-07-04

### Fixed
- `LC021` now handles EF Core named-filter `IgnoreQueryFilters(filterKeys)` calls safely: extension syntax rewrites back to the query receiver instead of the filter-key argument, while static extension syntax reports and rewrites to the explicit `source` query argument even when named arguments are reordered.

## [5.6.36] - 2026-07-04

### Fixed
- `LC002` still reports `Last`/`LastOrDefault` after inline `ToList`/`ToArray`/`AsEnumerable` boundaries, but no longer offers the move-before-materialization fixer for those ordering-sensitive terminals because rewriting them onto `IQueryable` can change runtime behaviour on unordered EF queries.

## [5.6.35] - 2026-07-04

### Fixed
- `LC037` now detects `StringBuilder` SQL assembled through separate `Append(...)` statements before `ToString()` reaches a raw SQL sink, including null-conditional appends, local dynamic and method-call append values, loop-carried and compound-assigned append locals, caught-throw continuations with exact, alias, ordinary base, user-defined base, and framework base exception catches, copied builder expressions, constructor copies from tainted builders, and conditional builder aliases, while keeping constant-only, branch-selected literal, path-dominated constant append-local overwrite, per-iteration constant reset, variable-capacity constructor, constant compound-assignment, terminating-branch local, fluent `Clear()` reset, try/catch-contained branch clear, catch-exiting throw, guaranteed `finally` clear, short-circuit reset, terminating guard, and definitely cleared builder flows precise.

## [5.6.34] - 2026-07-02

### Fixed
- `LC037` now guards raw-SQL local and `StringBuilder` resolution against self-referential assignments that previously caused analyzer StackOverflow failures while preserving the constructed-SQL diagnostic.
- `LC011` now guards malformed self-referential builder locals so incomplete `OnModelCreating` edits no longer crash analyzer execution.
- `LC003`, `LC008`, `LC026`, and `LC032` fixers now preserve semantics for constant-zero count comparisons, awaited receiver continuations, chained async invocations, and unsupported `ExecuteUpdate` receivers.
- `LC014` now reports string case-conversion predicates on EF Core async query terminals such as `AnyAsync` and `FirstOrDefaultAsync`.

## [5.6.33] - 2026-06-26

### Changed
- Added canonical SEO and authenticity surfaces for the official LinqContraband project, including a GitHub Pages-ready docs hub, crawler metadata, backlink kit, security policy, README safe-install guidance, and richer NuGet search metadata.

## [5.6.32] - 2026-06-26

### Changed
- `LC016` docs, README guidance, and executable sample now explain deterministic clock boundaries, injected-clock/testability guidance, `UtcNow` timestamp preference, and provider-specific server-clock alternatives while preserving the narrow local-variable fixer contract.

## [5.6.31] - 2026-06-26

### Changed
- `LC040` docs, README guidance, and executable sample now explain legitimate split workflows, why transactions and transparent query options do not change tracking mode, and why the rule remains manual-only. Test coverage now locks mixed-mode detection through `AsSplitQuery()`/`TagWith(...)` chains and explicit transactions.

## [5.6.30] - 2026-06-26

### Changed
- `LC031` docs, README guidance, and executable sample now explain the manual-only remediation contract for unbounded materialization, including intentional full scans, exports, streaming/batching, reviewed suppressions, and non-bounds such as `Where`, `Skip` without `Take`, `TakeLast`, `Chunk`, and transparent query options. Test coverage now locks query-syntax `DbContext.Set`, bounded query-syntax aliases, `Skip`-only, `TakeLast`, and `AsNoTracking` chains.

## [5.6.29] - 2026-06-26

### Fixed
- `LC029` now recognises statement-bodied interface-enumerable identity projections such as `items.Select(x => { return x; })`, preserves parenthesized/cast/null-forgiving fluent receivers, keeps static, concrete-enumerable, awaited-task, explicit-cast, and type-changing projection forms out of the safe-fixer path, and clarifies that explicit boundaries such as `AsEnumerable()` should be kept directly rather than marked with `Select(x => x)`.

## [5.6.28] - 2026-06-26

### Fixed
- `LC041` now treats null-conditional single-property reads, scalar chains, and method chains such as `user?.Name`, `user?.Name.Length`, and `user?.Name.Trim()` as the same single-scalar over-fetch pattern as direct `user.Name` chains, while keeping those conditional-access diagnostics manual-only so the fixer does not leave stale `?.` syntax behind.

## [5.6.27] - 2026-06-26

### Changed
- `LC022` docs, README guidance, and executable sample now describe the rule as an advisory query-shape review for modern EF Core correlated collection projections, including the direct-projection, split-query, DTO-contract, and narrow safe-fixer boundaries.

## [5.6.26] - 2026-06-26

### Fixed
- `LC043` now treats buffered locals captured by nested lambdas or local functions as additional uses, so the `await foreach` fixer is not offered when removing the buffer would break captured code.

## [5.6.25] - 2026-06-26

### Fixed
- `LC027` now recognises `HasForeignKey(...)` calls made through a single-assignment relationship-builder local, such as `var relationship = builder.HasOne(...).WithMany(...); relationship.HasForeignKey("CustomerShadowId");` and split `HasOne(...).WithOne(...).HasForeignKey(...)` continuations, so explicit shadow-FK configurations no longer receive a missing-FK false positive. Reassigned relationship-builder locals stay conservative and continue to require an explicit property or direct configuration proof.

## [5.6.24] - 2026-06-26

### Fixed
- `LC005` now detects a resetting `OrderBy` after a single-assignment sorted local, such as `var sorted = q.OrderBy(...); sorted.OrderBy(...)`. The existing `ThenBy` fixer is offered when the receiver still has an ordered type, including static `Enumerable`/`Queryable` syntax; widened locals report as manual fixes, and reassigned, deconstruction-written, or `out`/`ref`-written locals stay quiet so ambiguous local state does not produce path-sensitive false positives.

## [5.6.23] - 2026-06-26

### Fixed
- `LC045` now reports the full nested navigation path for parenthesized null-conditional regrouping such as `(order?.Customer)?.Address?.City`, including inline materializer and inherited-navigation forms, so an already-included `Customer` no longer hides the missing `Customer.Address` include. Conditional method-call results such as `(order?.Customer.GetDetached())?.Address` remain outside the queried receiver path.

## [5.6.22] - 2026-06-26

### Fixed
- `LC018` now offers a safe `SqlQueryRaw<T>` → `SqlQuery<T>` fixer for direct interpolated scalar/keyless query SQL, preserving generic type arguments while keeping quoted interpolation, concatenation, and raw-parameter shapes manual.

## [5.6.21] - 2026-06-25

### Changed
- `LC008` docs and sample now explain the mapped EF async counterpart families, no-async-equivalent guidance, query-expression translation boundaries, static-local-function scoping, non-async lambda/local-function fixer limits, cancellation-token non-goals, and scalar terminal sync-over-async examples.

## [5.6.20] - 2026-06-25

### Changed
- `LC002` docs and sample now explain `ToList()`/`ToArray()`/`AsEnumerable()` boundary differences, provider-safe lambda gates, intentional client-side continuation patterns, redundant materialization versus shape-changing materializers, and the narrow fixer contract for premature materialization chains.

## [5.6.19] - 2026-06-25

### Changed
- `LC004` docs now explain when to keep `IQueryable<T>` signatures versus explicitly materializing with `.ToList()`, plus forwarding-chain detection, expression-bodied and query-syntax consumption, safe deferred boundaries, source-body limits, nested local-function/lambda scoping, and the narrow fixer contract for `IQueryable` to `IEnumerable` leaks.

## [5.6.18] - 2026-06-23

### Changed
- `LC021` suppression-path validation now covers type-level `SuppressMessage`, static extension-call pragma suppression, `.editorconfig` severity suppression, and generated-code exclusion, complementing the existing direct diagnostic, EF/lookalike, `IEnumerable`, local pragma, and method-level `SuppressMessage` coverage. The docs now distinguish narrow reviewed suppressions from broader project-policy disablement for intentional `IgnoreQueryFilters` bypasses.

## [5.6.17] - 2026-06-23

### Fixed
- `LC001` code fix now handles static `Queryable` forms, including fully qualified `System.Linq.Queryable`, aliases to `System.Linq.Queryable`, reordered named `source:`/`outer:` arguments for operators such as `Where` and `Join`, ordered static continuations such as `ThenBy`, extension/static ordered source chains, and nested static continuations after the fixed operator, by rewriting safe operators to `Enumerable` and inserting an explicit `AsEnumerable()` source boundary. Bare static and alias fallbacks use `System.Linq.Enumerable` to avoid user-defined `Enumerable` collisions, semantic guards keep extension calls on receivers named `Queryable` on the extension-fixer path, static wrappers without `Enumerable` counterparts such as `Queryable.AsQueryable` stay on `Queryable`, upstream translatable static operators such as `Queryable.Take` remain server-side before the boundary, and complex source expressions such as awaited sources are parenthesized before `.AsEnumerable()`. The docs now clarify the client-evaluation trade-off, preferred SQL-translatable rewrites, mapped-function/projectable alternatives, intentional client-side filtering, reported query positions, and safe non-row-dependent helper cases.

## [5.6.16] - 2026-06-23

### Changed
- `LC003` existence-check validation now locks scalar expression contexts, including boolean assignments, return expressions, and async `LongCountAsync` replacement with `AnyAsync`. The docs now explain provider cost, supported comparison patterns, when `Count()` is still the correct API, and the exact fixer behaviour.

## [5.6.15] - 2026-06-23

### Changed
- `LC026` cancellation-token validation now locks multi-token fixer selection, including `ct` preference when `cancellationToken` is unavailable, readable property tokens, field-token replacement for `CancellationToken.None`, and named-default replacement when multiple tokens exist. The docs now explain the local token-selection contract, field/property handling, ambiguity boundaries, and why the fixer does not synthesize new tokens.

## [5.6.14] - 2026-06-23

### Changed
- `LC035` bulk execute validation now locks deeper filtered-local and reassignment paths, including overwritten earlier assignments, filtered-local conditional reassignments, multiple optional filtered narrowings, and unfiltered catch-path reassignment. The docs now explain every-path filtering, project-local `Where` lookalikes, and why the rule has no automatic fixer.

## [5.6.13] - 2026-06-23

### Changed
- `LC038` excessive eager-loading validation now locks threshold fallback/lowering, transparent EF query options, and non-EF `Include` lookalikes. The docs now explain intentional large-load cases, projection and split-query alternatives, and the LC006 cartesian-explosion boundary.

## [5.6.12] - 2026-06-23

### Changed
- `LC019` conditional `Include` validation now locks `ThenInclude` receiver conditionals, richer filtered Include negatives, and non-EF `ThenInclude` lookalikes. The docs now explain when to split the query, project the conditional shape, or eager-load both branches explicitly.

## [5.6.11] - 2026-06-23

### Changed
- `LC028` deep `ThenInclude` validation now locks sibling include-chain depth resets, per-chain reporting when multiple sibling chains exceed the threshold, and invalid `dotnet_code_quality.LC028.max_depth` fallback to the default threshold.

## [5.6.10] - 2026-06-23

### Changed
- `LC039` repeated `SaveChanges` docs now explain the intended batching shape, explicit EF Core transaction boundaries, branch/try/catch suppression rules, separate-context behaviour, executable-root scoping, and why the advisory remains manual-only.

## [5.6.9] - 2026-06-23

### Changed
- `LC034` and `LC037` raw SQL docs now clarify direct raw-execution interpolation/concatenation versus hidden constructed-SQL flows. The docs include parameterized rewrite guidance and analyzer-backed examples for `string.Format`, `string.Concat`, chained `StringBuilder`, and `SqlQueryRaw<T>` so users can choose the right safe API without assuming unsupported coverage.

## [5.6.8] - 2026-06-13

### Fixed
- `LC044` now detects silent-data-loss patterns where the mutation happens inside a **nested block that falls through to `SaveChanges`**. Previously the rule required the property mutation and `SaveChanges` to be in the same immediate block, so mutations inside `if`/`else`/`using`/`while` bodies before a later `SaveChanges` were missed. The reachability check now accepts ancestor/descendant block relationships and blocks on explicit `return`/`throw` terminators between the mutation and the save, while still excluding sibling branches (e.g., mutation in one `if` branch and save only reachable through the other). Added regression coverage for nested-scope mutations, foreach-mutation on an `AsNoTracking()` queryable source, and nested `if` inside a foreach body.

## [5.6.7] - 2026-06-10

### Fixed
- `LC008` no longer fires on a sync EF call inside a **`static` local function** declared in an `async` method, closing the deferred orphaned-warning item from the 2026-06-04 rerun. A `static` local function cannot capture the enclosing async context and `await` is illegal inside it, so the suggested "use the async counterpart and await it" was impossible to apply — the warning's only resolutions were suppression or removing `static`. Both async-context walks (operation tree and the semantic-model fallback) now treat a non-async `static` local function as a hard synchronous boundary. Capturing (non-static) local functions and lambdas inside async methods still flag — that refactoring debt is the rule's deliberate signal — and an `async static` local function still flags because it can await. This completes the 2026-06-10 Medium shortlist — every planned hardening item from the health-audit batch has now shipped (see the 5.6.2 through 5.6.6 entries above).

## [5.6.6] - 2026-06-10

### Fixed
- `LC009` no longer suggests `AsNoTracking()` for methods that **mutate the materialized entity and commit through a helper**, closing the deferred cross-method residual from the 2026-06-04 rerun. The write detection only saw same-body `SaveChanges`/`Add`/`Update`/`Remove` calls, so the common repository shape — materialize, set a property, call `_repository.Commit()` — was flagged read-only, and applying the suggested `AsNoTracking()` would have silently dropped the save. A property mutation of the materialized result now marks the body as a write path: on the result local (only when the materializer's value is stored directly into a single-assignment local), on `foreach` iteration variables over the result or the inline materializer, and inline on the materializer itself (`db.Users.First(...).Name = x`); compound assignment and `++`/`--` count. Guards keep the rule honest: a DTO populated from entity values, a repointed local mutated while it held a different object, and indexer element replacement (`users[0] = new User()`) all still report. The caller-mutates-the-returned-entity case remains documented as unprovable locally.

## [5.6.5] - 2026-06-10

### Fixed
- `LC025` is now **path-aware** for conditionally reassigned locals, closing a false positive deferred since the 2026-06-04 rerun: `var user = db.Users.First(); if (flag) user = db.Users.AsNoTracking().First(); db.Users.Update(user);` no longer fires — on the `flag=false` path the entity is tracked, so the verdict depends on the path taken. The latest assignment before the write decides alone only when it is unconditional; a conditional latest is compared against the latest *unconditional fallback* (the state if the branch is skipped), with superseded history — e.g. an initial `= null` overwritten before the branch — excluded so it cannot manufacture false ambiguity. A disagreeing fallback or a conditional-only origin set stays quiet (the same ambiguity trade-off the silent-write rule makes for multiply-assigned locals); unconditional latest assignments, agreeing fallbacks, and same-branch reassign+write shapes all keep firing.

## [5.6.4] - 2026-06-10

### Fixed
- `LC012` no longer lets an unrelated `SaveChanges` hide the RemoveRange→`ExecuteDelete` suggestion. Two false negatives are closed: a save on a **provably different context instance** (`db1.Users.RemoveRange(q); db2.SaveChanges();` — proof requires both receivers to resolve, through single-assignment alias chains, to two *different freshly-created* locals) and a save in a **mutually exclusive `if`/`else` or `switch` branch**, which can never run after the `RemoveRange` in the same execution. Precision stays deliberately conservative: aliased contexts (`var db2 = db1;`) still suppress, context parameters/fields/factory results and reassigned locals can never be proven distinct and suppress, a `switch` containing `goto case`/`goto default` keeps suppressing because sections can flow into each other, and a try-block `RemoveRange` with a catch-side save stays quiet because the try may throw *after* the removals were registered. (Closes the LC012 open follow-up from the 2026-06-04 rerun.)

## [5.6.3] - 2026-06-10

### Fixed
- `LC023` no longer suggests `Find`/`FindAsync` for entities with a visible **global query filter**. `Find` checks the change tracker before querying, and a tracker hit bypasses `HasQueryFilter` (verified against EF Core 9's `EntityFinder`: only the database fallback runs through the filtered query root) — so the `FirstOrDefault(x => x.Id == id)` → `Find(id)` rewrite could silently return an already-tracked soft-deleted or other-tenant row the filtered query excluded. The gate keys on the `DbSet`'s entity type rather than the key's declaring type (an `Id` inherited from a `BaseEntity` doesn't dodge it), walks base types because EF declares filters on the hierarchy root and propagates them to derived entities, and recognises `OnModelCreating`, `EntityTypeBuilder<T>` configuration classes, and the non-generic `modelBuilder.Entity(typeof(X)).HasQueryFilter(...)` form. Lookalike `HasQueryFilter` methods on non-EF builder types and filters on unrelated entities do not suppress. A filter configured in another assembly remains invisible — documented as a manual review point. (Closes the hazard deferred from the 2026-06-04 Round-2 rescan.)

## [5.6.2] - 2026-06-10

### Fixed
- `LC045` was silent on the **null-conditional spellings** of shapes it already flags in plain form, contradicting the rule doc's "null-guarded access still fires" contract. Five false negatives are closed: chained inline access on the materializer (`db.Orders.FirstOrDefault()?.Customer?.Name` — the entry-property descent stopped at a nested conditional access), the mixed chain (`FirstOrDefault()?.Customer.Address?.City`), a method call on the navigation (`FirstOrDefault()?.Customer.Clear()` — the `WhenNotNull` arm is an invocation), conditional element access on the result (`orders?[0].Customer.Name`), and a local initialized from a conditional indexer (`var o = orders?[0];`). The fixes descend only strictly-shrinking sides of the operation tree (a conditional access's `Operation` side, an invocation's receiver), so the 5.6.0 recursion-crash class cannot recur — and the same adversarial pass that found these confirmed no surviving crash shapes across deep `?.` chains, `!`/cast mixes, interpolation, and nested conditional access in arguments, all locked in by regression tests. Residual documented polish: a parenthesized regrouping (`(order?.Customer)?.Address?.City`) truncates the reported path to `Customer`.

## [5.6.1] - 2026-06-10

### Fixed
- **Critical:** `LC045` crashed the C# compiler itself — an uncatchable `StackOverflowException`, not a containable AD0001 — whenever a materialized entity's navigation was read through a **chained null-conditional** (`order?.Customer?.Name`). The conditional-access placeholder of the middle link belongs to the *outer* `?.`, but the receiver resolution matched the first conditional access found walking up, which for nested accesses returned the property reference being resolved itself, so `TryGetAccessPath` recursed on its own input until the stack overflowed and the whole `csc` process died (reported against 5.6.0 from a production codebase; the mixed shape `order?.Customer.Address?.City` triggered the same crash via mutual recursion). Placeholders now resolve to the conditional access reached from its `WhenNotNull` side, bounding the walk by expression size. Un-crashing these shapes also unmasked one false positive, fixed here too: `order?.Items?.Add(x)` — the null-guarded spelling of the collection-mutation pattern the rule deliberately ignores — is now recognised by the mutator-receiver check instead of being flagged as an unloaded read. Anyone who disabled the rule with `dotnet_diagnostic.LC045.severity = none` to unblock builds can re-enable it on this version.

## [5.6.0] - 2026-06-09

### Added
- New rule `LC045` **Missing Include: navigation accessed on materialized entity** (Reliability, Warning, code fix). Detects the canonical EF Core read-side bug: a DbSet-rooted query is materialized (`ToList`, `FirstOrDefault`, …) and a navigation property of the result is then read without a matching `Include`/`ThenInclude` — an N+1 query per access under lazy-loading proxies, or a silent `null`/empty collection without them. The code fix inserts `.Include(x => x.Nav)` (with `.ThenInclude` for nested paths) immediately before the materializer and supports FixAll. Detection is deliberately conservative: only shape-preserving operator chains rooted in a `DbSet` property/field qualify; `Select`/`Join`/custom operators, unparseable (dynamic-string) Includes, reassigned locals, and any escape of the result (returned, passed as an argument, lambda-captured, stored non-locally) silence the query. Navigation writes (`o.Customer = c`, deconstruction, collection `Add`/`Remove`) are recognised as relationship fix-up, and an in-memory assignment satisfies later reads of that path. Null-guarded reads (`!= null`, `?.`) flag on purpose — the guard itself triggers the lazy load or hides the always-null bug. Hardened pre-release by adversarial FP/FN hunting and four cross-model review rounds (constructor bodies, `?.`-guarded and indexed access, `nameof`, keyword-named navigations, static LINQ call syntax, DbSet fields).

### Fixed
- `LC006` include-path parsing (now shared with `LC045` via `IncludePathParser`): a mid-path cast or null-forgiving operator in an Include lambda — `Include(o => o.Customer!.Address)`, `Include(o => ((Derived)o.Nav).Child)` — previously parsed as a silently truncated path (`Address`), which could mis-group sibling collections; the full path is now parsed, and an unrecognised lambda shape fails the parse instead of truncating.

## [5.5.13] - 2026-06-04

### Fixed
- Closed an `LC026` false negative: a `CancellationToken` stored in a **field** or surfaced through a readable **property** is now recognised as an available token, so an EF async call (`ToListAsync`, `SaveChangesAsync`, …) that omits it is flagged and the fixer passes it by name. The scope lookup previously accepted only `ILocalSymbol`/`IParameterSymbol`, so the common injected-token (`private CancellationToken _ct;`) and `IHostApplicationLifetime.ApplicationStopping`-style patterns were silently missed even though the token is readily passable. The fixer references the member by bare name, which binds to `this.<member>`; write-only properties are skipped. Added field-token and property-token regression tests plus a fixer test confirming the field reference is inserted. (Found by a second adversarial false-negative rescan.)

## [5.5.12] - 2026-06-04

### Fixed
- Stopped `LC041` from flagging a primary-key lookup written as `users.Where(x => x.Id == id).First()`. The single-entity over-fetch exemption for a key lookup only inspected the **terminal** operator's inline predicate (`First(x => x.Id == id)`), so the equivalent form with the key predicate on an upstream `Where` step — one of the most common EF read patterns — was reported even though it is the same single-row-by-key fetch. The exemption now also walks the receiver chain for a `Where` whose predicate is a primary-key equality. A non-key `Where` (e.g. `Where(x => x.IsActive)`) is not exempt and still reports. Added a `Where`-step PK no-trigger test and a non-key-`Where` still-fires guardrail. (Found by a second adversarial false-positive rescan; the rarer hoisted-`Expression`-predicate form remains a documented follow-up.)

## [5.5.11] - 2026-06-04

### Fixed
- Closed an `LC001` false negative on aggregate selectors. A local (non-translatable) method inside a `Sum`/`Average`/`Min`/`Max` selector — `db.Users.Sum(u => Weight(u.Age))` — was not flagged because those four operators were missing from the rule's translation-critical method set, even though `Queryable.Sum`/`Average`/`Min`/`Max(source, selector)` translate to SQL `SUM`/`AVG`/`MIN`/`MAX(expr)` and a source-defined method in the selector forces client evaluation (or throws) — the same smell LC001 already catches in `Where`/`OrderBy`/`GroupBy`/predicates. The four aggregate operators are now in the set. Added a parameterized regression test across all four. (Found by a second adversarial false-negative rescan.)

## [5.5.10] - 2026-06-04

### Fixed
- Closed two false negatives surfaced by the adversarial rescan. **LC004** (IQueryable passed as IEnumerable) now follows a C# **query expression** back to its source parameter: `int CountUsers(IEnumerable<User> users) { var q = from u in users where u.Id > 5 select u; return q.Count(); }` enumerated the parameter in memory but was silently exempt, because a query expression surfaces as an `ITranslatedQueryOperation` that the parameter-source walk did not unwrap (the fluent equivalent `users.Where(...).Count()` already fired). The walk now unwraps the lowered query, so query-syntax enumeration of an `IEnumerable` parameter is detected too. **LC044** (AsNoTracking entity mutated then SaveChanges) now treats a **compound assignment** (`entity.Prop += 1`) and an **increment/decrement** (`entity.Prop++`) as a mutation, not only a plain `=` assignment — each silently loses on an untracked entity. Added a query-expression leak test (LC004) and compound-assignment + increment tests (LC044). (Found by an adversarial false-negative rescan.)

## [5.5.9] - 2026-06-04

### Fixed
- Stopped `LC035` from reporting the common "base filter + optional extra narrowing" shape. `var q = db.Users.Where(...); if (flag) q = q.Where(...); q.ExecuteDelete();` was flagged because the local-filter check inspected only the **latest** assignment before the bulk call — the conditional `q = q.Where(...)` inside the `if` — and bailed to "no filter" because it sat inside a branch, ignoring that the unconditional base already had a `Where` (a false positive: every path is filtered). The check now resolves the latest **unconditional** base assignment (which every control-flow path passes through) and treats the local as filtered only when that base **and** every later **conditional** reassignment are filtered. A conditional path that reassigns to an unfiltered query (`if (flag) q = db.Set<User>();`) still reports, as does an unfiltered base. Added the base-filter-plus-narrowing no-diagnostic test and an unfiltered-conditional-reassignment guardrail. (Found by an adversarial false-positive rescan; also closes the audit's LC035 filtered-local/reassignment follow-up.)

## [5.5.8] - 2026-06-04

### Fixed
- Closed an `LC007` false negative on the deconstruction `foreach` shape. `foreach (var (a, b) in pairs) { db.Users.Find(a); }` is a `ForEachVariableStatementSyntax` — a distinct syntax node from a regular `foreach` (`ForEachStatementSyntax`) — so the per-iteration check (and the loop-kind label) skipped it, and an N+1 inside a tuple/deconstruction loop was silently missed even though the fluent equivalent fired. The per-iteration check and the loop-kind helper now match the shared `CommonForEachStatementSyntax` base, covering both the regular and deconstruction `foreach` shapes (and their `await` forms). Added a deconstruction-foreach regression test. (Found by an adversarial false-negative rescan.)

## [5.5.7] - 2026-06-04

### Fixed
- Stopped two false positives caused by unrecognized mutually-exclusive branch shapes. `LC040` (mixed tracking modes) now treats the two arms of a ternary (`readOnly ? db.Users.AsNoTracking().ToList() : db.Users.ToList()`) as mutually exclusive, just like `if`/`else` and `switch` — only one arm materializes, so the scope never mixes tracking modes. `LC039` (repeated SaveChanges) now treats a `SaveChanges` in a `try` block and one in a `catch` clause as mutually exclusive: the catch save runs only if the try save threw (and so never completed), making it a compensating/retry save rather than a batchable repeat (two different `catch` clauses are likewise exclusive). A materializer in a ternary's **condition** (LC040) and a `finally` save (LC039) are deliberately still counted, because they always run. Added ternary (LC040), try/catch (LC039), and a try/finally-still-fires guardrail (LC039) regression tests and documented both boundaries. (Found by an adversarial false-positive rescan.)

## [5.5.6] - 2026-06-04

### Fixed
- Stopped `LC025` and `LC044` from firing on a query whose tracking is restored by a trailing `AsTracking()`. Both rules scanned the query chain for `AsNoTracking()` but ignored a later `AsTracking()`, so `db.Users.AsNoTracking().AsTracking().First()` followed by `Update()` (LC025) or a property mutation + `SaveChanges()` (LC044) was wrongly flagged — even though EF Core applies the **last** tracking directive (`AsTracking()` overwrites the earlier `AsNoTracking()`), leaving the entity tracked so the write is correct. Both chain scans now honour the last directive: the first directive encountered walking up the receiver chain (the one applied last) decides the effective mode. `AsNoTracking().AsTracking()` no longer reports; the reverse `AsTracking().AsNoTracking()` (untracked) and a plain `AsNoTracking()` still report. Added override and reverse-order regression tests to both rules and documented the last-directive-wins behavior. (Found by an adversarial false-positive rescan.)

## [5.5.5] - 2026-06-04

### Fixed
- Closed a raw-SQL injection false negative: `LC037` now treats `SqlQueryRaw<T>` (the EF7+ scalar/keyless raw-SQL query on `DbContext.Database`, `db.Database.SqlQueryRaw<T>(sql)`) as a construction sink. A SQL string built with `string.Format(...)`, `string.Concat(...)`, a `StringBuilder`, or an aliased local and passed to `SqlQueryRaw` was invisible to the entire Raw SQL neighborhood — `LC018` covers only its interpolation and `+` concatenation (added in 5.4.13), and `LC037` did not list `SqlQueryRaw` among its sinks (it had only `FromSqlRaw`/`ExecuteSqlRaw`/`ExecuteSqlRawAsync`). `SqlQueryRaw` is now in `LC037`'s target set, and LC037's existing deferral keeps interpolation and `+` concatenation owned by `LC018` (no double-report). Added `string.Format` and `string.Concat` positive tests for the facade `SqlQueryRaw` sink plus interpolation-deferral and constant-SQL guardrails, and documented the new sink and the LC018/LC037 boundary

## [5.5.4] - 2026-06-04

### Fixed
- Stopped `LC002` from reporting a misleading "redundant" materialization when the **source** is a keyed (`ToDictionary`/`ToDictionaryAsync`) or grouped (`ToLookup`) materializer. `db.Users.ToDictionary(u => u.Id).ToList()` and `db.Users.ToLookup(u => u.Age).ToList()` were flagged as "`ToList` is redundant because `ToDictionary`/`ToLookup`", but the trailing call is a genuine shape change — it yields `List<KeyValuePair<,>>` and `List<IGrouping<,>>` respectively, not a redundant re-materialization of the same sequence, and removing the keyed/grouped source would change the result type. These sources are now treated like the existing de-duplicating set sources (`ToHashSet().ToList()`) and left quiet: no diagnostic and no fix are offered. Genuinely redundant non-keyed collapses (`ToArray().ToList()`, `ToList().ToHashSet()`) still report and fix, and the separate premature-materialization diagnostic for a keyed source feeding a query operator or terminal aggregate (`ToDictionary(...).Where(...)`, `ToDictionary(...).Count()`) is unaffected. Added `ToDictionary().ToList()` and `ToLookup().ToList()` no-diagnostic regression tests and documented the keyed/grouped-source exclusion

## [5.5.3] - 2026-06-04

### Fixed
- Closed three `LC015` false negatives by extending the ordering-dependent operator set. `ElementAt`/`ElementAtOrDefault` (EF Core 6+ translates these to `OFFSET … FETCH`, which is non-deterministic without an ordering), their async forms `ElementAtAsync`/`ElementAtOrDefaultAsync`, and the async `LastAsync`/`LastOrDefaultAsync` (the async twins of the already-flagged `Last`/`LastOrDefault`, which EF reverses the ordering for and throws without one) are now reported on an unordered EF `IQueryable`. The code fix also offers `OrderBy(x => x.<key>)` for these operators, subject to the same single-detectable-primary-key safety gate as `Skip`/`Take`/`Last` (it still declines composite-, `[Keyless]`-, and unconventional-key entities). `TakeLast`/`SkipLast` are deliberately **not** flagged: EF Core cannot translate them to SQL at all — they throw "could not be translated" even after an `OrderBy` (dotnet/efcore#25242, #17065) — so "add an ordering" would be wrong advice; the operator itself, not a missing ordering, is the problem. Added analyzer tests for each new operator, an `OrderBy`-upstream guardrail, a `TakeLast`-not-flagged guardrail, and `ElementAt`/`LastAsync` fixer tests, and documented the expanded operator set and the `TakeLast`/`SkipLast` exclusion in the rule doc

## [5.5.2] - 2026-06-04

### Fixed
- Taught `LC020` to flag a `StringComparison` overload whose query column flows through a method **argument**, not only its receiver. The check only inspected the call's receiver, so `db.Users.Where(u => "admin".Contains(u.Name, StringComparison.OrdinalIgnoreCase))` — where the constant is the receiver and the column `u.Name` is the searched-for argument — was missed even though EF Core cannot translate the overload and throws at runtime on the default relational providers (a false negative; the same class as a column reaching the comparison through the receiver). Detection now considers the receiver **and every argument** for query-parameter dependence, so the argument-derived form reports and the existing fixer drops the `StringComparison` argument (`"admin".Contains(u.Name)`). Constant and captured-local arguments stay quiet (the comparison is parameter-independent), as do in-memory/`IEnumerable` calls and custom `IQueryable` helpers that take delegate predicates. Added an argument-derived positive test, a captured-local-argument guardrail, and a fixer test; the rule doc now documents the argument-derived shape and the verified provider behavior (default providers throw; Npgsql maps `OrdinalIgnoreCase` to `ILIKE`; Pomelo MySQL opts in via `EnableStringComparisonTranslations`)

## [5.5.1] - 2026-06-03

### Fixed
- Stopped four code fixers from injecting a lone `LF` line into a `CRLF` file. `LC010` (move `SaveChanges` after a do-while loop), `LC011` (insert a missing `Id` primary-key property), `LC016` (extract `DateTime.Now`/`UtcNow` to a local), and `LC027` (insert an explicit foreign-key property) each terminated the statement or member they add with a hard-coded line feed — `SyntaxFactory.ElasticLineFeed` or `SyntaxFactory.EndOfLine("\n")`. On a CRLF document that produced a single LF line amid CRLF lines (a mixed-line-ending file that surfaces as a spurious source-control diff and can trip formatters/linters), and it made every one of those fixer tests fail on a CRLF (Windows) checkout while passing on LF CI. The fixers now terminate the inserted/moved node with an end-of-line trivia harvested from the document itself (the new `SyntaxTriviaExtensions.GetDocumentEndOfLine`), so the applied fix preserves the file's existing line endings — CRLF or LF — on every platform. The two fixers that emit their newline as *leading* trivia after a warning comment already produce the platform newline through the formatter and are unaffected. Added a cross-platform `LC010` regression test that pins CRLF on both the source and the expected fix regardless of how the repository is checked out

## [5.5.0] - 2026-05-29

### Added
- Added an automatic code fixer for `LC032` (previously report-only). It rewrites a *proven* tracked bulk-update loop — a `foreach` over an EF query that assigns only scalar properties on the iteration variable, immediately followed by `SaveChanges`/`SaveChangesAsync` on the same context — into a single set-based `ExecuteUpdate`/`ExecuteUpdateAsync` call, collapsing N materialized-and-tracked rows and N `UPDATE` statements into one server-side `UPDATE`. The fixer builds one `SetProperty` per assigned property by reusing the loop variable name as the lambda parameter (so each assignment's target and value transplant verbatim) and prepends a `// Warning: ExecuteUpdate runs immediately and bypasses change tracking and entity callbacks.` comment. It mirrors the bulk-delete (`ExecuteDelete`) fixer's safety model: the trailing `SaveChanges` is left in place (it commits nothing for the converted rows but still flushes unrelated pending changes), inside an `async` context it prefers the awaited `ExecuteUpdateAsync` overload and carries the cancellation token from the awaited `SaveChangesAsync(token)` onto it, inline materializers (`ToList`/`ToArray`/`ToListAsync`/`ToArrayAsync`, including the awaited form) are stripped, and duplicate property writes collapse last-write-wins. The fixer declines — leaving the advisory diagnostic to a manual rewrite — for local-variable sources (which would orphan the local or produce a type-invalid receiver), an observed `SaveChanges` result (`return`/assignment, which the rewrite would change), a value that reads a property written earlier in the same iteration (`ExecuteUpdate` evaluates every value against the original row), and an async context with no suitable `ExecuteUpdateAsync` overload, so it never emits a blocking, uncompilable, or behaviour-changing rewrite. Added 23 fixer tests across the rewrite and decline shapes (incl. token propagation, top-level programs, and write-then-read dependencies)

## [5.4.13] - 2026-05-29

### Fixed
- Extended `LC018` to detect `SqlQueryRaw<T>` — the EF7+ scalar/keyless raw-SQL query on `DbContext.Database` (`db.Database.SqlQueryRaw<int>($"… {id}")`). It takes a raw `string sql` and is an equal SQL-injection sink to `FromSqlRaw`, but was invisible to the rule because the name gate only matched `FromSqlRaw` and the receiver gate only accepted `IQueryable`/`DbSet` (never the `DatabaseFacade` receiver). The name gate now also matches `SqlQueryRaw`, the receiver gate accepts a `DatabaseFacade`, and the diagnostic message is method-aware (it points to `SqlQuery` — the safe `FormattableString` sibling — for `SqlQueryRaw`, and `FromSqlInterpolated` for `FromSqlRaw`). The safe `SqlQuery<T>` sibling and constant-only interpolation stay quiet. The auto-fix continues to rewrite `FromSqlRaw` → `FromSqlInterpolated`; the `SqlQueryRaw` diagnostic is report-only (switch to `SqlQuery` by hand). Added interpolation, concatenation, safe-sibling, and constant-interpolation regression tests for the facade form
- Stopped the `LC015` code fix from offering a misleading `OrderBy(x => x.Id)` on a `[Keyless]` entity (a SQL view or query type) that happens to expose an `Id` property. A keyless entity has no primary key, so `Id` is an ordinary column and ordering by it is no more deterministic than not ordering — yet the convention-driven key lookup returned `"Id"` and the fixer inserted a partial fix that masks the very non-deterministic pagination LC015 exists to flag. The fixer now bails when the entity carries `Microsoft.EntityFrameworkCore.KeylessAttribute`; the diagnostic still reports (it is a real ordering smell, just one with no safe single-column fix). Added a `[Keyless]`-entity no-fix regression test. (A composite key configured purely via Fluent `OnModelCreating` remains undetectable from source — the analyzer cannot read the model — so that case stays a documented manual check in the LC015 no-fix guidance.)
- Fixed three `LC024` false positives on group projections that EF Core 9 translates server-side. The rule only accepted an aggregate applied *directly* to the grouping parameter, so `g.Any()`, a filtered aggregate `g.Where(o => o.Amount > 0).Count()`, and an aggregate over a projection `g.Select(o => o.Amount).Sum()` were all flagged as non-translatable even though EF emits `EXISTS`/`COUNT`/`SUM` for them. `Any`/`All` (and their `Async` forms) are now recognised aggregates, and the rule now accepts an aggregate whose receiver chain roots at the grouping parameter through translatable operators (`Where`, `Select`, `OrderBy`/`OrderByDescending`/`ThenBy`/`ThenByDescending`, `Distinct`). A chain that terminates in a non-aggregate still reports: a bare `g.Where(p)`, a materializer `g.Select(s).ToList()`, and an element accessor `g.OrderBy(s).First()` all remain flagged. The exemption is deliberately conservative — it covers only **invocation-free** predicates/selectors (member access, comparisons, arithmetic). Any method call inside a `Where`/`Select` lambda (a local function, a user method, or a non-translatable BCL overload such as `o.Name.Equals(s, StringComparison.OrdinalIgnoreCase)`) keeps the chain reported, so the rule never assumes translatability it cannot prove. Added four false-positive regression tests (incl. a query-syntax variant) and four guardrails (terminal non-aggregate × 2, a user-method selector, and a BCL-method predicate)
- Fixed an `LC006` false positive (and a matching false negative) when an Include chain is split across a single-assignment local. The receiver-chain walk stopped at a local reference, so `var q = db.Users.AsSplitQuery(); q.Include(u => u.Orders).Include(u => u.Roles)` reported a Cartesian explosion even though the split was effective — punishing the exact code a developer writes to fix the warning — while `var q = db.Users.Include(u => u.Orders); q.Include(u => u.Roles)` (one query, two sibling collections) was missed. The walk now resolves a single-assignment local back to its assigned value (via `LocalAssignmentCache`), so a prior-statement `AsSplitQuery()` is honoured and sibling Includes split across the local are detected. Reassigned or ambiguous locals stay conservative. Added two `AsSplitQuery()`-on-local false-positive guardrails and a sibling-split-across-local positive test
- Stopped `LC002` from reporting a redundant materialization — and offering a fix that silently changed de-duplication — when the **source** materializer is a de-duplicating set. The redundant fix removes the *previous* materializer, so `db.Users.ToHashSet().ToList()` warned that `ToList` was "redundant because `ToHashSet`" and rewrote the chain to `db.Users.ToList()`, dropping the `ToHashSet()` and silently returning duplicate rows (`ToHashSet().ToArray()` and `ToImmutableHashSet().ToList()` had the same defect). Even a trailing set was unsafe: `ToHashSet(StringComparer.OrdinalIgnoreCase).ToHashSet()` would collapse to a default `ToHashSet()`, changing which duplicates are removed. A set source is therefore no longer treated as redundant. Genuinely redundant collapses with a non-set source still report and fix, preserving the trailing call's own arguments: `ToArray().ToList()` → `ToList()` and `ToList().ToHashSet()` → `ToHashSet()`. Added four false-positive regression tests (set-then-list/array, immutable-set-then-list, comparer-set-then-set) and a `ToList().ToHashSet()` collapse guardrail
- Fixed an `LC014` false positive introduced with the 5.4.12 method-argument walk. The walk followed *every* argument of a method, so a column reaching a **numeric/positional** argument made a constant-receiver result look column-derived: `db.Users.Where(u => "CONSTANT".PadRight(u.Name.Length).ToLower() == "x")`, `"HELLO".Substring(0, u.Age).ToLower()`, and `"HELLO".Remove(u.Age).ToLower()` all warned even though the lowercased text comes entirely from the constant and the column only controls length/position — casing never touches a column, so there is no sargability impact. The walk now follows only arguments that can carry the column's text into the result — `string` and other reference-typed arguments, plus a `char` argument (which contributes a character, e.g. `string.Concat(u.Name[0]).ToUpper()`, `"x".Replace('x', u.Name[0]).ToLower()`) — and skips value-type arguments that only control position or format (`int`/`bool`/enum such as `StringComparison`). Genuine crimes where a column flows through a **string** argument still fire, including on a constant receiver (`"prefix".Replace("x", u.Name).ToLower()`) and the existing `string.Concat(u.A, u.B)` / `string.Join(...)` / `string.Format(...)` cases. Added three numeric-argument false-positive regression tests, a string-argument positive guardrail, and two `char`-argument positive guardrails
- Stopped `LC005` from crashing the analyzer (`AD0001` / `InvalidCastException`) on query-comprehension syntax. Two separate `orderby` clauses (`from x in xs orderby a orderby b select x`) lower to `OrderBy(...).OrderBy(...)` — the same reset smell as the fluent form — but their operation syntax is an `OrderingSyntax`, not an `InvocationExpressionSyntax`. The analyzer hard-cast that node, so every compilation containing the shape threw and the rule produced no result. The cast is now guarded, and the reset is reported at the offending `orderby` clause instead of being swallowed by the crash (closing a false negative the crash had masked). The code fix is still offered only for the fluent form — query syntax has no method call to rewrite to `ThenBy`, so the query-syntax diagnostic is report-only. Added a crash/regression test for the two-clause shape plus guardrails that a single `orderby` and a multi-key `orderby a, b descending` clause (which lowers to `OrderBy(...).ThenBy(...)`) stay quiet

## [5.4.12] - 2026-05-28

### Fixed
- Taught `LC014` to detect case conversion on a value that depends on the query parameter through a method's **arguments**, not only its receiver. Previously `ReceiverDependsOnParameter` only followed an invocation's instance, so `db.Users.Where(u => string.Concat(u.First, u.Last).ToLower() == "x")` was missed — the `string.Concat(...)` is a static call with no instance, and its column-derived arguments were never inspected (a false negative; the `ToLower` still defeats sargability). The walk now also checks invocation arguments. Constant-only values such as `string.Concat("a", "b").ToLower()` stay quiet because no argument depends on the parameter. Added a positive regression test and a constant-argument guardrail

## [5.4.11] - 2026-05-28

### Fixed
- Stopped `LC031` from treating `Chunk` as a bounding operator. There is no `Queryable.Chunk`, so `db.Users.Chunk(1000).ToList()` binds to `Enumerable.Chunk` and materializes the entire table before partitioning — the `size` argument bounds the chunk size, not the rows fetched. `Chunk` was whitelisted alongside `Take`/`First`, suppressing the diagnostic on exactly the unbounded load LC031 exists to catch (a false negative). Real bounding operators before the chunk (e.g. `Take(100).Chunk(10)`) still suppress correctly. Added a false-negative regression test plus a `Take`-then-`Chunk` guardrail, and refreshed the affected sample's expected diagnostics (a `users.Chunk(5).ToList()` sample now also reports `LC031`, as it should)

## [5.4.10] - 2026-05-28

### Fixed
- Stopped `LC025` from firing on entities that come from a `Select` projecting to a newly-constructed object (e.g. `AsNoTracking().Select(u => new User { ... }).First()` followed by `Update`/`Remove`). EF Core never change-tracks instances constructed in a projection, so they are untracked regardless of `AsNoTracking()` — the anti-pattern does not apply and the suggested "remove `AsNoTracking()`" fix would not have helped. Only the outermost projection (the one that determines the materialized result) is considered, so identity and navigation projections (`Select(u => u)`, `Select(u => u.Manager)`) and a constructed wrapper that is later unwrapped back to the entity (`Select(u => new { E = u }).Select(x => x.E)`) all continue to report. Added regression tests for the constructed-projection false positive, the identity-Select guardrail, and the wrap-then-unwrap shape

## [5.4.9] - 2026-05-28

### Changed
- Taught `LC009` to recognise the generic-repository `context.Set<T>()` read path as an EF source. The chain walker previously stepped past a `DbSet`-returning invocation to the `DbContext` receiver, so `db.Set<User>().ToList()` (and `Set<T>().Where(...).ToList()`) never got the AsNoTracking suggestion — a false negative on one of the most common EF read patterns. The code fix now finds the EF source by its semantic type instead of by syntax, so `AsNoTracking()` is placed on the `Set<User>()` call rather than mis-placed onto the `DbContext` (which would not compile). Added analyzer regression tests for the `Set<T>()` source (with and without `Where`) plus guardrails that `Set<T>()` with `AsNoTracking()`/`Select(...)` stays quiet, a fixer test for the `Set<T>()` placement, and expanded the LC009 doc and README to cover the recognised sources and when `AsNoTracking()` is unsafe (identity resolution, deferred/cross-method mutation, re-attach)

## [5.4.8] - 2026-05-28

### Changed
- Made the `LC012` code fix async-aware: inside an `async` method, lambda, or local function it now rewrites `RemoveRange(query)` to `await query.ExecuteDeleteAsync()` instead of the synchronous `query.ExecuteDelete()`, which would have injected a blocking sync-over-async database call — the exact smell `LC008` flags. The synchronous rewrite is unchanged when the nearest enclosing function is synchronous (including a sync local function nested inside an async method), and in an async context where no `ExecuteDeleteAsync` overload is available the fix is withheld rather than emit a blocking call. Added regression tests for the async-await rewrite, the sync-local-function-inside-async boundary, and the no-async-overload refusal, and documented the code-fix behaviour and `ExecuteDelete` safety contract in the LC012 doc and README

## [5.4.7] - 2026-05-14

### Changed
- Hardened the `LC015` code fix so it no longer registers on composite-keyed entities, including the EF Core 7+ class-level `[PrimaryKey(...)]` attribute form. `TryFindPrimaryKey` returns the first `[Key]`-annotated property and never sees siblings, so without the new composite-key gate the fixer would offer a partial-key `OrderBy(x => x.<firstKey>)` that does not guarantee deterministic pagination — the very behaviour LC015 exists to surface. Added regression tests for the two-`[Key]`-property shape and the `[PrimaryKey]` class-level shape, and widened the LC015 doc to enumerate when the fixer registers, when it does not, and how to pick a stable key in the no-fix case (composite keys, time-series tiebreakers, floating-point/text-collation anti-patterns)

## [5.4.6] - 2026-05-14

### Changed
- Widened the `LC006` doc (~95 lines) to spell out the `AsSplitQuery()` tradeoff (extra roundtrips, per-statement plan cost, snapshot consistency, transaction scope), when Cartesian is legitimately the right call, and the rule boundary against LC028 (depth) and LC038 (count); added a lock-in test that two sibling reference navigations stay quiet because reference navigations cannot Cartesian-explode

## [5.4.5] - 2026-05-14

### Changed
- Locked in `LC018` constant-only interpolation FP coverage with regression tests for `const` locals, numeric literals, multi-hole constant interpolations, and `nameof(...)` staying quiet, plus boundary positives for mixed-constant-and-runtime interpolation holes and `static readonly` field references still triggering; tightened the LC018 doc to spell out the `IOperation.ConstantValue` safe-shape gate and the upstream-construction LC037 boundary

## [5.4.4] - 2026-05-14

### Changed
- Locked in `LC034` provider/API variant coverage with regression tests for the static-extension `RelationalDatabaseFacadeExtensions.ExecuteSqlRaw(...)` and `ExecuteSqlRawAsync(...)` forms firing on unsafe interpolation while the safe siblings `ExecuteSql`/`ExecuteSqlAsync` stay quiet on the same static-extension shapes

## [5.4.3] - 2026-05-14

### Changed
- Locked in `LC018` provider/API variant coverage with regression tests for the `DbSet<T>` instance call, the `DbContext.Set<T>()` chain, and the static-extension `RelationalQueryableExtensions.FromSqlRaw(...)` form firing on unsafe interpolation while the safe `FromSqlInterpolated` sibling stays quiet on the same receiver shapes

## [5.4.2] - 2026-05-14

### Changed
- Hardened the `LC010` `do`-loop fixer so it no longer offers to move `SaveChanges`/`SaveChangesAsync` out of a `do` loop that is itself nested inside another loop, because the rewrite would leave the save inside the outer loop and still trigger `LC010`; locked in async-`SaveChangesAsync`, multiple-save, non-final-statement, and only-statement edge cases as fixer regression tests

## [5.4.1] - 2026-05-14

### Changed
- Hardened `LC039` transaction-scope analysis so repeated saves inside C# 8+ `using` and `await using` local declarations of an EF Core transaction stay quiet, while non-transaction `using` declarations and saves preceding the declaration continue to report
- Raised the `AnalyzerPerformanceTests` per-test budget from 10s to 30s so cold-JIT CI Linux runners no longer cancel stress-source analyzer runs mid-compilation; local M-class hardware still completes each stress source in well under a second

## [5.4.0] - 2026-05-04

### Changed
- Hardened `LC039` repeated-save analysis so mutually exclusive `if`/`else` branches no longer produce false repeated-save diagnostics
- Expanded `LC039` branch regression coverage and refreshed docs/analyzer-health guidance for the advisory rule boundary
- Hardened `LC040` mixed tracking analysis so mutually exclusive branch choices do not warn while later shared materialization is still compared against every reachable earlier mode
- Expanded `LC031` unbounded materialization analysis to follow query-syntax expressions and query-syntax aliases while preserving bounded and LINQ-to-Objects negatives
- Hardened `LC010` SaveChanges-in-loop analysis to report local functions that are invoked from loops, with async-foreach/do-loop coverage and conservative no-fix regression coverage
- Hardened `LC035` bulk execute filtering so query-syntax `where` clauses and filtered query-syntax locals suppress full-table warnings while unfiltered query syntax still reports
- Hardened `LC012` `RemoveRange(...)` analysis so mixed or multiple arguments no longer produce misleading `ExecuteDelete()` diagnostics or fixes
- Hardened `LC036` thread-work analysis to inspect directly passed local-function callbacks while preserving fresh-context and direct-call negatives
- Expanded `LC024` GroupBy regression coverage for query-syntax grouping, LINQ-to-Objects query-syntax negatives, and static `Enumerable` aggregate exemptions
- Hardened `LC038` excessive eager-loading analysis to count include chains after transparent query-shaping calls such as filters, ordering, split-query, tracking, and tags
- Hardened `LC015` deterministic pagination analysis to follow ordered and paginated query aliases when suppressing redundant warnings or reporting misplaced sorting
- Hardened `LC014` string case-conversion analysis so `Join`/`GroupJoin` key-selector diagnostics are tied to the actual EF-backed source and in-memory inner/projection selectors stay quiet
- Hardened `LC018` and `LC034` raw-SQL namespace checks so EF Core lookalike namespaces do not produce false security diagnostics
- Hardened `LC025` no-tracking write analysis so project-local `AsNoTracking` lookalikes do not produce false update/remove diagnostics
- Hardened shared primary-key detection for `LC023` so fake same-name `[Key]` attributes no longer drive unsafe `Find(...)` suggestions
- Hardened `LC043` async-buffering analysis so custom non-`IAsyncEnumerable<T>` `ToListAsync`/`ToArrayAsync` methods stay quiet
- Expanded `LC021` intentional-bypass coverage for reviewed `SuppressMessage` suppressions alongside narrow pragma suppression guidance
- Hardened `LC012` ExecuteDelete availability checks so EF Core lookalike namespaces do not enable misleading `RemoveRange(...)` diagnostics
- Hardened `LC010` SaveChanges-in-loop analysis so catch-guarded retry loops that exit after a successful save do not produce N+1 write diagnostics
- Hardened `LC035` bulk execute filtering so EF Core lookalike namespaces do not produce full-table operation diagnostics
- Hardened `LC004` deferred-execution leak analysis to treat known BCL collection constructors as proven `IEnumerable<T>` consumption while preserving custom-constructor negatives
- Hardened `LC031` unbounded materialization analysis to treat `DbContext.Set<TEntity>()` chains and aliases as DbSet-backed query sources
- Hardened `LC025` no-tracking write analysis to report explicit `Entry(entity).State = Modified` or `Deleted` write paths while leaving non-write states quiet
- Hardened `LC039` repeated-save analysis so mutually exclusive `switch` sections do not produce false repeated-save diagnostics
- Hardened `LC040` mixed tracking analysis so mutually exclusive `switch` sections do not produce false mixed-mode diagnostics
- Hardened `LC018` and `LC034` raw-SQL interpolation checks so no-hole and constant-only interpolated strings no longer produce security diagnostics
- Hardened the `LC021` fixer so static `EntityFrameworkQueryableExtensions.IgnoreQueryFilters(query)` calls are reduced back to the query expression instead of the extension type name
- Hardened `LC035` bulk execute filtering so project-local `Where` lookalikes do not hide unfiltered `ExecuteDelete` or `ExecuteUpdate` calls
- Hardened `LC039` repeated-save analysis so repeated saves inside the same explicit transaction `using` block stay quiet while unrelated `using` blocks still report
- Hardened `LC040` mixed tracking analysis so reassigned local query aliases resolve by assignment-before-use, catching same-context mode changes without conflating different contexts
- Hardened `LC018` and `LC034` raw-SQL checks so EF-namespace helpers on unrelated receiver types do not produce security diagnostics
- Hardened the `LC012` fixer so a later `SaveChanges()` outside the immediate block still suppresses the automatic `ExecuteDelete()` rewrite
- Hardened `LC010` retry-loop handling so catch-guarded save attempts that `return` immediately after success stay quiet while conditional returns still report
- Hardened `LC035` bulk execute filtering so straight-line filtered local reassignments suppress full-table warnings while conditional reassignments still report
- Hardened `LC012` so `RemoveRange(...)` calls followed by `SaveChanges()` in the same executable body no longer produce timing-sensitive `ExecuteDelete()` diagnostics

## [5.3.1] - 2026-04-27

### Changed
- Hardened `LC021` regression coverage for non-EF lookalikes, local suppression, and fixer query-chain preservation to prevent false-positive and unsafe-fix regressions
- Clarified `LC021` intentional-bypass documentation and refreshed analyzer-health priority after the noise-boundary coverage

## [5.3.0] - 2026-04-27

### Changed
- Hardened `LC001`, `LC002`, `LC007`, `LC008`, `LC016`, and `LC022` against production false positives from in-memory LINQ, translated query subqueries, and captured-value helpers
- Changed `LC022` to an advisory `Info` diagnostic with modern EF Core wording for nested collection projection materialization
- Coalesced repeated `LC016` clock diagnostics within one query lambda and expanded the fixer to replace matching repeated clock accesses together
- Updated docs, analyzer health, and regression coverage for production feedback patterns

## [5.2.21] - 2026-04-27

### Changed
- Suppressed `LC008` code fixes when adding `await` would produce invalid C# in query clauses or non-async scopes
- Kept `LC008` fixes enabled for legal initial `from` collection expressions while preserving diagnostics in unsupported contexts
- Added fixer regression coverage for query `let`, query `where`, initial `from`, and non-async lambda contexts

## [5.2.20] - 2026-04-27

### Changed
- Cached shared local assignment scans for `LC002` and `LC031` query provenance resolution to reduce repeated full-method operation walks
- Cached `LC044` SaveChanges analysis inputs per executable root so local declarations, foreach loops, mutations, reattach calls, and prior SaveChanges calls are scanned once
- Updated package release notes for the analyzer scan-cache performance pass

## [5.2.19] - 2026-04-27

### Changed
- Cached `LC015` local query-source resolution per operation block and added a self-referential local guard to prevent pathological query provenance loops
- Cached `LC007` local write scans and threaded analyzer cancellation through query provenance analysis to reduce repeated full-method traversal
- Collapsed `LC017` whole-entity usage scans and syntax fallback scans into bounded single passes with cancellation checks
- Added analyzer performance coverage for `LC007`, `LC015`, and `LC017` hot paths

## [5.2.18] - 2026-04-27

### Changed
- Limited `LC023` Fluent `HasKey` discovery to analyzer-observed invocations and the current syntax tree instead of scanning every syntax tree in the compilation
- Indexed `LC011` and `LC027` model type lookups so repeated Fluent API/configuration resolution no longer walks every source type for each lookup
- Added multi-syntax-tree performance coverage for `LC023`

## [5.2.17] - 2026-04-27

### Changed
- Cached `LC023` primary-key metadata per compilation so primary-key query detection no longer rescans every syntax tree for every matching query
- Cached `LC011` and `LC027` schema/model configuration scans and added cancellation checks to prevent pathological build-time analyzer work in large projects
- Fixed `LC028` and `LC038` option-cache access under concurrent analyzer execution

## [5.2.16] - 2026-04-27

### Changed
- Hardened `LC011` primary-key detection so fake same-name attributes no longer suppress diagnostics, ignored `Id` properties are not treated as mapped keys, and unapplied `IEntityTypeConfiguration<T>` classes no longer leak across contexts
- Expanded `LC011` Fluent API coverage for scoped builder variables, chained builder calls, local applied configuration instances, current-assembly configuration scanning, and inferred `OwnsOne`/`OwnsMany` owned types
- Restricted the `LC011` fixer so it does not generate duplicate `Id` members when an invalid `Id` already exists

## [5.2.15] - 2026-04-27

### Changed
- Hardened `LC010`, `LC012`, `LC014`, `LC015`, `LC016`, `LC018`, `LC020`, `LC021`, `LC022`, `LC023`, `LC024`, `LC031`, `LC034`, `LC037`, `LC039`, and `LC040` analyzer/fixer safety to reduce false positives, false negatives, and unsafe automated rewrites
- Removed the unsafe `LC014` code fix and documented manual remediation where database collation, provider translation, and index design determine the correct fix
- Added sample and architecture governance for safe sample coverage, catalog metadata, package release-note drift, and editorconfig severity coverage

## [5.2.14] - 2026-04-26

### Changed
- Hardened `LC020` string comparison overload analysis so diagnostics require a `System.Linq.Queryable` expression lambda and a direct or nested string receiver that depends on a query parameter
- Suppressed false positives for captured local and constant string comparisons inside query predicates, and for custom `IQueryable` helpers that execute delegate predicates outside expression-tree translation
- Hardened the `LC020` fixer to remove the semantically bound `StringComparison` argument and expanded analyzer/fixer coverage for `Contains`, `StartsWith`, and `EndsWith`

## [5.2.13] - 2026-04-26

### Changed
- Hardened `LC028` deep `ThenInclude` reporting so each over-threshold chain reports once at the first threshold breach instead of repeating on later links
- Added `dotnet_code_quality.LC028.max_depth` for teams with reviewed deep eager-loading graphs
- Expanded `LC028` default, configured-threshold, and non-EF boundary coverage, and refreshed docs/analyzer-health status to describe the manual-only review contract

## [5.2.12] - 2026-04-26

### Changed
- Hardened `LC019` conditional Include analysis so ternary and null-coalescing receivers inside longer Include paths are reported
- Preserved safe filtered Include predicate and non-EF `Include` extension boundaries to avoid broad false positives
- Expanded `LC019` Include/ThenInclude coverage and refreshed docs/analyzer-health status to describe the manual-only query-shape guidance

## [5.2.11] - 2026-04-26

### Changed
- Hardened `LC027` Fluent API configuration analysis so `HasOne(...).WithMany(...).HasForeignKey(...)` suppresses diagnostics for the dependent reference navigation instead of the inverse collection
- Hardened the `LC027` fixer so optional nullable navigations generate nullable FK properties and non-`int` primary key types are preserved
- Expanded `LC027` fixer and model-configuration coverage and refreshed docs/analyzer-health status to describe the safer schema guidance

## [5.2.10] - 2026-04-26

### Changed
- Added rule quality governance for package metadata, code-fix exports, documentation drift, and sample diagnostic expectations
- Moved sample diagnostic expectations into `samples/LinqContraband.Sample/sample-diagnostics.json` so cross-rule sample coverage is versioned with the samples
- Corrected package repository metadata and analyzer help links to point to the public `georgepwall1991/LinqContraband` repository

## [5.2.9] - 2026-04-26

### Changed
- Hardened `LC041` so entity property writes and entity escape patterns are not treated as safe single-scalar consumption
- Narrowed the `LC041` fixer to `First`/`Single` materializers, avoiding unsafe `*OrDefault` rewrites that can change no-row/null behavior
- Expanded `LC041` analyzer and fixer coverage and refreshed docs/analyzer-health status to describe the safer projection contract

## [5.2.8] - 2026-04-26

### Changed
- Hardened the `LC023` fixer so awaited async primary-key lookups with explicit cancellation tokens rewrite to the token-preserving `FindAsync(object[] keyValues, CancellationToken)` overload
- Suppressed unsafe `LC023` async fixes when the original call is not awaited, avoiding `Task<T>` to `ValueTask<T>` return-shape changes
- Refreshed `LC023` docs and analyzer-health status to describe the safer fixer contract

## [5.2.7] - 2026-04-24

### Changed
- Hardened the `LC026` fixer so explicit `default`, named default, and `CancellationToken.None` arguments are replaced with the available token instead of appending a duplicate argument
- Added `LC026` fixer coverage for omitted, local, preferred-name, default-token, and named-token argument shapes
- Refreshed `LC026` docs and analyzer-health status to describe the safer fixer contract

## [5.2.6] - 2026-04-24

### Changed
- Hardened the `LC005` fixer so explicit generic `OrderBy<TSource, TKey>(...)` calls keep their type arguments when rewritten to `ThenBy<TSource, TKey>(...)`
- Added dedicated `LC005` fixer coverage for explicit generic calls and descending sort chains
- Refreshed `LC005` docs and analyzer-health status to reflect the tighter fixer contract

## [5.2.5] - 2026-04-24

### Changed
- Hardened `LC025` local-origin analysis so diagnostics follow the nearest previous assignment or declaration instead of treating later no-tracking assignments as write-path evidence
- Expanded `LC025` coverage for query aliases, `UpdateRange`/`RemoveRange` collection locals, assignment-based fixes, foreach collection fixes, and tracked-reassignment false-positive boundaries
- Refreshed `LC025` docs/sample guidance and analyzer-health status to reflect the safer analyzer and fixer contract

## [5.2.4] - 2026-04-24

### Changed
- Hardened `LC024` GroupBy projection analysis around EF translation boundaries, including local helpers over grouped keys, client-only string comparison calls, direct group object construction, and nested non-aggregate group projections
- Expanded `LC024` safe coverage for aggregate-only projections and LINQ-to-Objects grouping boundaries
- Expanded `LC036` thread-work detection to cover `Task.Factory.StartNew(...)`, `Thread`, timer callbacks, async lambdas, and captured `DbContext` members
- Added `LC036` safe-pattern coverage for factory-created contexts, scoped context creation inside callbacks, scalar-only capture, and work scheduled after materialization without context capture
- Refreshed LC024/LC036 docs and analyzer-health status to mark both rules as reference-quality examples

## [5.2.3] - 2026-04-24

### Changed
- Hardened `LC018` `FromSqlRaw(...)` security analysis with named `sql:` argument handling, nested direct concatenation coverage, and LC037 boundary tests for alias-owned constructed SQL
- Narrowed the `LC018` fixer so it only rewrites direct interpolated-string calls with no additional raw SQL parameters
- Narrowed `LC012` to query-shaped `RemoveRange(...)` sources and guarded its fixer from materialized lists, arrays, tracked collections, and params-style entity arguments
- Expanded `LC030` lifetime review coverage for singleton service/implementation registrations, configured long-lived base/interface types, and factory/scope-safe patterns
- Added `LC035` coverage for async bulk execute calls, chained query filters, and simple filtered local query initializers
- Hardened `LC043` so cancellation-token buffer calls and reused buffers are not rewritten to `await foreach`
- Refreshed analyzer-health and per-rule docs to distinguish shipped behavior from remaining roadmap items

## [5.2.2] - 2026-04-24

### Changed
- Reworked `LC006` cartesian explosion analysis around EF query-chain navigation paths so diagnostics now focus on distinct sibling collection includes instead of every later include call
- Added `LC006` coverage for nested sibling `ThenInclude` collections, filtered includes, resolvable string include paths, duplicate include suppression, and final `AsSplitQuery()` / `AsSingleQuery()` ordering
- Hardened the `LC006` fixer so it inserts `AsSplitQuery()` at the stable query root and replaces an effective downstream `AsSingleQuery()` when needed
- Updated `LC006` docs, README guidance, and sample wording to describe sibling collection risk and the preferred split-query placement

## [5.2.1] - 2026-04-24

### Changed
- Hardened `LC002` continuation reporting with a conservative provider-safety classifier for lambda-based operators, suppressing client-only continuations such as delegated predicates, local/source methods, `Regex`, and `StringComparison` string calls
- Expanded `LC002` analyzer and fixer coverage for safe lambda continuations, no-fix boundaries, and mixed fix-all behavior
- Updated `LC002` docs and README reliability notes to describe the new precision-first continuation gate

## [5.2.0] - 2026-04-24

### Changed
- Upgraded `LC030` to strict-by-default lifetime review: it now reports only when a `DbContext` member or constructor injection is paired with proven long-lived evidence such as hosted services, conventional middleware, `AddHostedService<T>()`, `AddSingleton(...)`, or explicit singleton `DbContext` registrations
- Added `LC030` `.editorconfig` knobs for expanded name-based review and project-specific long-lived base/interface types
- Marked `LC030` as manual-only in the catalog/docs and removed its stale architecture-changing fixer surface
- Hardened `LC034` so `ExecuteSqlRaw` / `ExecuteSqlRawAsync` diagnostics resolve the `sql` parameter by symbol binding, including named/reordered arguments, and report the correct safe replacement API for sync vs async calls
- Tightened the `LC034` fixer contract so it only rewrites direct interpolated-string calls with no additional raw SQL parameters
- Expanded `LC034` analyzer/fixer coverage around nested concatenation, named arguments, parameterized raw SQL, safe `ExecuteSql*` calls, and `LC037`-owned constructed SQL flows
- Updated `LC034` docs, README guidance, sample code, and analyzer-health status to match the hardened behavior

## [5.1.0] - 2026-04-20

### Added
- Added `LC044` — AsNoTracking query mutated then SaveChanges. Detects silent data-loss bugs where an entity loaded with `AsNoTracking()` is mutated and `SaveChanges` is called on the same context without any re-attach (`Update`/`Attach`/`Entry(entity).State = Modified|Added`). The chain is provable intra-procedurally; the analyzer uses single-assignment dataflow, same-context linkage, block-ancestor reachability, and explicit re-attach gating to keep false positives at zero across the 43 existing sample files
- Added docs, sample, and test coverage (18 tests: 7 positive, 11 FP-exclusion scenarios) for `LC044`

### Changed
- Grouped `LC044` under the `Change Tracking & Context Lifetime` domain in `RuleCatalog`, the rule-catalog documentation, and the README

## [5.0.4] - 2026-04-01

### Changed
- Grouped analyzer source folders by domain under `src/LinqContraband/Analyzers/` while preserving analyzer ids, namespaces, tests, samples, and docs layout
- Extended `RuleCatalog` metadata with analyzer source paths so architecture tests validate the grouped source layout through the catalog
- Refreshed contributor guidance to explain the grouped source layout and catalog-driven placement rules for future analyzers
- Refreshed `.complexity-log.md` path references to match the new grouped analyzer source folders

## [5.0.3] - 2026-04-01

### Added
- Added a central `RuleCatalog` / `RuleCatalogEntry` metadata registry for all 43 rules, including explicit domain taxonomy, docs/sample paths, fixer metadata, and no-fix rationales
- Added architecture governance tests that verify analyzer, test, sample, docs, and fixer layout consistency across the repository
- Added `docs/rule-catalog.md` and filled the previously missing docs for `LC019`, `LC022`, `LC024`, `LC027`, `LC028`, and `LC031`

### Changed
- Split the shared `AnalysisExtensions` chokepoint into concern-specific partial files for invocation, traversal, reference, and symbol analysis helpers
- Moved cross-rule suite tests into `tests/LinqContraband.Tests/Architecture/` to give governance/integration coverage an explicit home
- Updated `README.md`, `CONTRIBUTING.md`, and `docs/adding_new_analyzer.md` to document the rule neighborhoods, repository contract, and catalog-driven workflow
- Added descriptor-alignment tests so catalog metadata is checked against analyzer descriptors instead of drifting as a parallel registry

## [5.0.2] - 2026-03-29

### Changed
- Upgraded `Microsoft.CodeAnalysis.Analyzers` from `3.11.0` to `5.3.0` in the analyzer and test projects while keeping `Microsoft.CodeAnalysis.CSharp` and `Microsoft.CodeAnalysis.CSharp.Workspaces` pinned at `4.3.0` to preserve host compatibility

### Fixed
- Removed the sample-project `NU1903` vulnerability warning by overriding the `net8.0` transitive `Microsoft.Extensions.Caching.Memory` dependency from `8.0.0` to `8.0.1`
- Kept the sample diagnostic contract stable after the sample dependency override and analyzer-package refresh

## [5.0.1] - 2026-03-29

### Changed
- Tightened raw-SQL analyzer ownership so `LC018` and `LC034` own direct interpolated-string and direct non-constant `+` call-site patterns, while `LC037` stays focused on broader constructed-SQL flows such as aliases, `string.Format(...)`, `string.Concat(...)`, and `StringBuilder`
- Tightened projection analyzer ownership so grouped `GroupBy(...).Select(...)` materializers are owned by `LC024`, while `LC022` remains focused on ordinary `IQueryable.Select(...)` collection materializers
- Updated the sample diagnostics verifier and rule documentation to enforce and describe the exact post-dedup diagnostic contract

### Fixed
- Reduced duplicate diagnostics on direct raw-SQL interpolation/concatenation call sites without weakening the broader constructed-SQL coverage
- Reduced duplicate diagnostics for grouped projection materializers and kept the more specific `GroupBy` guidance as the only emitted rule for that shape

## [5.0.0] - 2026-03-29

### Added
- Added `LC034` through `LC043`, expanding the shipped analyzer set from 33 rules to 43 rules
- Added per-rule docs and sample-project coverage for `LC034`, `LC035`, `LC036`, `LC037`, `LC038`, `LC039`, `LC040`, `LC041`, `LC042`, and `LC043`
- Added targeted analyzer and fixer coverage for the new rule set, including guarded no-fix scenarios where the fixer contract is intentionally narrow

### Changed
- `LC001` now caches `DbFunctionAttribute` / `ProjectableAttribute` symbol lookups once per compilation instead of resolving them per invocation
- `LC003` now covers `Count()` / `LongCount()` / async variants compared against `0`, including `== 0` and reversed-operand forms, and the fixer rewrites those cases to `!Any()` / `!AnyAsync()`
- `LC009` now caches write-operation detection per executable root instead of rescanning the same method body for each materializer
- `LC025` now walks the operation tree without materializing `Descendants().ToList()` for the whole root
- `LC026` now uses a single-pass in-scope `CancellationToken` search and prefers `cancellationToken`, then `ct`, before falling back to the first available token
- `LC027` now respects `HasForeignKey(...)`, `OwnsOne(...)`, `OwnsMany(...)`, and `IEntityTypeConfiguration<T>` / `OnModelCreating(...)` Fluent API patterns when deciding whether a navigation is missing an explicit FK
- Updated README, `.editorconfig`, and sample-project coverage to reflect the 43-rule surface, new advisory defaults, and tunable thresholds for `LC038` and `LC042`

### Fixed
- `LC023` continues to suppress `Find(...)` suggestions when the query chain already contains `AsNoTracking()`, preventing an impossible “fix” that would force tracking
- Expanded edge-case and fixer coverage for the newly hardened foundation rules, including regression cases for `LC009`, `LC025`, `LC026`, and `LC027`

## [4.7.1] - 2026-03-18

### Changed
- `LC001` now trusts methods explicitly marked with `Microsoft.EntityFrameworkCore.DbFunctionAttribute` or `EntityFrameworkCore.Projectables.ProjectableAttribute`, so known translatable helpers no longer trigger client-evaluation warnings
- Updated the LC001 README and rule documentation to document the explicit translation-marker exemptions and their intended scope

### Fixed
- `LC001` no longer flags `DbFunction`-mapped methods or `Projectable` methods, including reduced extension-method calls that should translate through EF Core or Projectables
- Expanded LC001 regression coverage with should-not-report tests for official translation markers and should-report guards for unannotated helpers and lookalike third-party `ProjectableAttribute` types

## [4.7.0] - 2026-03-18

### Added
- Added `LC033`, an advisory analyzer and fixer that upgrades provably read-only `private static readonly HashSet<T>` membership caches to `FrozenSet<T>` and `ToFrozenSet(...)` on supported target frameworks

### Changed
- `LC033` reports only when the cache initializer is fixer-safe and every source reference in the compilation is a direct `Contains(...)` use outside expression-tree contexts
- Updated the README, rule documentation, and sample project to publish the new analyzer/fixer surface and raise the documented rule count from 32 to 33

### Fixed
- Hardened the `LC033` fixer to preserve semantic type binding under aliases and colliding imports by generating rewritten type syntax from Roslyn symbols instead of minimally qualified text
- Hardened `LC033` initializer classification to reject static `Enumerable.ToHashSet(...)`-style receivers semantically rather than relying on syntax-string comparison
- Expanded `LC033` regression coverage with should-report, should-not-report, fix, no-fix, Fix All, alias, and colliding-type-name scenarios

## [4.6.0] - 2026-03-18

### Added
- Added `LC032`, an advisory analyzer that flags provable EF bulk scalar update loops and recommends `ExecuteUpdate` or `ExecuteUpdateAsync` when the loop source, scalar assignments, and trailing `SaveChanges` call are all proven within the same executable root

### Changed
- Expanded `LC032` query-shape support to cover modern EF query steps such as `IgnoreAutoIncludes` and `DbContext.Set<T>()`, while keeping the rule silent for ambiguous provenance and behavior-changing loop bodies
- Updated the README, rule documentation, and sample project to publish the new analyzer surface and raise the documented rule count from 31 to 32

### Fixed
- Expanded `LC032` regression coverage for synchronous and asynchronous save paths, intervening statements, enum assignments, and non-reporting cases where `ExecuteUpdate` is unavailable or the loop semantics are not provably safe to suggest

## [4.5.1] - 2026-03-18

### Changed
- `LC013` now traces returned `IQueryable` and `IAsyncEnumerable` values back through single-assignment local aliases within the same executable root before deciding whether a disposed-context leak is proven
- `LC013` now evaluates conditional, coalesce, and switch-expression returns branch by branch, so alias-based unsafe arms are reported without broadening the rule beyond its deferred-query contract
- Updated the LC013 README and rule documentation to describe alias-aware provenance tracking, `IAsyncEnumerable` coverage, and the analyzer-only remediation guidance

### Fixed
- `LC013` no longer treats arbitrary disposed locals as query origins; it now reports only when the deferred return is rooted in a disposed EF `DbContext`
- `LC013` now ignores nested local-function and lambda returns, avoiding false positives when the outer method still materializes the query before exiting
- Expanded LC013 regression coverage with should-report and should-not-report tests for aliases, composed aliases, async escapes, mixed branch returns, materialized locals, nested-return suppression, and non-`DbContext` disposed origins

## [4.5.0] - 2026-03-18

### Added
- `LC004` now has a guarded fixer that materializes proven generic query sources with `.ToList()` at the call site

### Changed
- `LC004` now uses compilation-cached same-compilation analysis to prove whether an `IEnumerable` parameter is actually consumed before reporting
- `LC004` only reports when the callee body is inspectable and the parameter is proven hazardous through direct enumeration, terminal/materializing `Enumerable` usage, or forwarding into another proven sink
- Updated the LC004 README and rule documentation to describe the proof-based reporting model and explicit caller-side materialization fix

### Fixed
- `LC004` no longer flags framework sinks, delegate invocations, methods without source bodies, pure pass-through helpers, or already materialized arguments
- Expanded LC004 regression coverage with should-report, should-not-report, fix, and no-fix scenarios for the new analyzer and fixer contract

## [4.4.0] - 2026-03-14

### Added
- `LC007` now has a conservative fixer for unconditional strongly-typed explicit-loading loops, rewriting `Reference(...).Load/LoadAsync` and `Collection(...).Load/LoadAsync` to eager loading with `Include(...)`

### Changed
- `LC007` now models loop-time database execution explicitly, classifying direct `Find`, explicit loading, navigation-query materialization, EF query materialization, and EF set-based executors
- `LC007` now reports only when both the EF-backed origin and the per-iteration execution are provable, including `DbContext.Set<T>()`, navigation `Query()`, single-assignment local hops, and set-based executors such as `ExecuteDelete` and `ExecuteUpdate`
- Updated the LC007 README and rule documentation to describe the execution-focused scope, intentional ignore cases, and the narrow fixer contract

### Fixed
- `LC007` no longer treats plain `IQueryable` shapes, `AsQueryable()`-backed LINQ-to-Objects queries, fields/properties/parameters with ambiguous provenance, or multi-assignment locals as definite N+1 database execution
- `LC007` no longer flags loop-source materialization that happens once before iteration, such as the `ToList()` in `foreach (var item in query.ToList())`
- Expanded LC007 regression coverage with should-report, should-not-report, fix, and no-fix scenarios across sync, async, navigation, and set-based execution paths

## [4.3.1] - 2026-03-13

### Fixed
- `LC002` now reports only approved post-materialization continuations with clear `IQueryable`-safe equivalents instead of broadly treating later `Enumerable` usage as suspicious
- `LC002` follows single-assignment locals and collection constructors when the `IQueryable` origin is provable, and stays silent for ambiguous local, field, and property provenance
- `LC002` redundant-materialization detection is now a first-class path with safe fixes only for analyzer-proven inline rewrites or direct redundant materializer pairs
- `LC002` no longer treats `Distinct` as reorder-safe, avoiding result-shape changes between LINQ-to-Objects equality and provider-side distinct semantics
- Expanded LC002 regression coverage for should-report, should-not-report, fix, no-fix, and fix-all scenarios, and aligned README/rule docs with the shipped contract

## [4.3.0] - 2026-03-13

### Changed
- `LC006` now evaluates `Include` chains from a single root query, respects `AsSplitQuery()` and explicit `AsSingleQuery()` overrides, and reports clearer navigation names in diagnostics
- `LC006` fixer now inserts `AsSplitQuery()` at a stable root-query location instead of patching only the flagged include segment
- `LC007` now distinguishes accessor-only explicit-loading APIs from real database work, so `Entry(...).Reference(...)` and `Entry(...).Collection(...)` alone are no longer treated as N+1 execution
- `LC010` still reports `SaveChanges()` and `SaveChangesAsync()` inside loops, but the fixer now appears only when the loop structure makes hoisting the save safely semantics-preserving
- `LC017` now uses cached body analysis instead of repeated whole-method scans and tracks downstream member usage more accurately, including conditional-access and indexer-driven access
- `LC022` now requires collection materializers inside `Select` to actually derive from the projection parameter or a query-built subexpression before reporting

### Fixed
- `LC002` now uses a stricter provenance model: it reports only approved post-materialization continuations with a clear `IQueryable`-safe equivalent, follows single-assignment locals and collection constructors, and stays silent on ambiguous multi-assignment or unsupported provenance shapes
- `LC002` redundant-materialization reporting is now first-class, and the fixer only appears for analyzer-proven inline rewrites or direct redundant materializer pairs
- `LC006` include-chain analysis now validates EF extension methods semantically, reducing false positives from lookalike method names
- `LC007` no longer flags pure query construction inside loops unless that chain definitely materializes or loads from the database
- `LC017` fixer now stays disabled for compile-risky cases such as explicit entity collection types or usages that escape simple property reads
- `LC022` fixer now stays disabled when removing the materializer would break object-initializer or DTO member type expectations
- Expanded regression coverage for LC002, LC006, LC007, LC010, LC017, and LC022, including new should-report, should-not-report, and safe/no-fix scenarios

## [4.2.0] - 2026-03-13

### Changed
- `LC015` now reports a single primary unordered-pagination diagnostic per fluent chain instead of stacking duplicate warnings on both `Skip(...)` and `Take(...)`
- `LC015` now prefers the more specific misplaced-ordering diagnostic when the chain already contains `Skip(...).OrderBy(...)` or `Take(...).OrderBy(...)`
- `LC017` now offers only the safe anonymous-type projection fixer; the compile-breaking direct projection action has been removed
- `LC030` now targets likely long-lived service shapes such as hosted services and conventional middleware patterns instead of treating every stored `DbContext` as suspicious
- Updated `LC030` docs and README guidance to frame the diagnostic as an advisory lifetime review for long-lived types

### Fixed
- Reduced `LC015` false-positive noise in pagination chains that previously produced redundant diagnostics for the same query
- Added `LC030` guardrails so generic service classes and scoped `IMiddleware` implementations are no longer flagged by default
- Hardened `LC030` review coverage with positive tests for hosted services and conventional middleware plus negative tests for obvious scoped or generic cases
- Removed the last `LC017` fixer path that intentionally produced uncompilable code and deleted the corresponding permissive test coverage
- Expanded targeted analyzer/fixer tests so the tightened heuristics are covered by both should-report and should-not-report scenarios

## [4.1.0] - 2026-03-13

### Changed
- Rebalanced advisory diagnostics `LC009`, `LC017`, `LC023`, `LC026`, `LC029`, and `LC030` to `Info` severity by default
- Updated README and rule docs to distinguish advisory hints from higher-confidence warnings
- `LC030` messaging now frames stored `DbContext` members as a lifetime review hint rather than a proven singleton bug

### Fixed
- `LC009` now suppresses diagnostics for ambiguous externally supplied query sources and avoids aggregate-only materialization noise
- `LC023` is limited to direct `DbSet` primary-key lookups with safer fixer registration for supported invocation shapes only
- `LC026` now reports only when a usable `CancellationToken` is actually in scope and only offers a fix in those cases
- Disabled the `LC030` architecture-changing fixer for heuristic lifetime diagnostics
- Standardized `using` insertion across several fixers to avoid root-replacement edits clobbering queued syntax changes

## [4.0.0] - 2026-02-05

### Added
- **LC019**: Conditional Include Expression — detects ternary/null-coalescing inside Include/ThenInclude (always throws at runtime)
- **LC022**: ToList/ToArray Inside Select Projection — detects collection materializers inside Select on IQueryable (forces client evaluation)
- **LC022 Fixer**: Removes the redundant materializer call inside the projection
- **LC024**: GroupBy Non-Translatable Projection — detects non-aggregate access to group elements in GroupBy().Select()
- **LC027**: Missing Explicit Foreign Key Property — detects navigation properties without corresponding FK properties (Info severity)
- **LC027 Fixer**: Inserts an explicit FK property above the navigation, inferring type from the entity's PK
- **LC028**: Deep ThenInclude Chain — detects ThenInclude chains deeper than 3 levels (over-fetching indicator)
- **LC031**: Unbounded Query Materialization — detects ToList/ToArray on DbSet chains without Take/First/Single bounds (Info severity)
- Sample projects for all 6 new analyzers

## [3.1.0] - 2026-02-05

### Fixed
- LC030 fixer: rewrites method/property usages from stored `DbContext` member access to short-lived contexts created via `IDbContextFactory<T>.CreateDbContext()`
- LC030 fixer: converts expression-bodied methods that use the flagged member into block bodies with scoped context creation
- LC030 fixer: targets the exact flagged field declarator when multiple variables share one field declaration
- LC030 fixer: ensures `using Microsoft.EntityFrameworkCore;` is added even when the file previously had no `using` directives

### Added
- LC030 fixer tests for end-to-end usage rewrites in block-bodied methods, expression-bodied methods, and property-based usage scenarios

## [3.0.0] - 2026-02-05

### Added
- **LC010 Fixer**: Moves `SaveChanges()`/`SaveChangesAsync()` to after the enclosing loop
- **LC021 Fixer**: Removes `.IgnoreQueryFilters()` from query chains
- **LC029 Fixer**: Removes redundant `.Select(x => x)` from query chains
- **LC030 Fixer**: Changes DbContext fields/properties to `IDbContextFactory<T>`, renames fields/params, updates constructor assignments (block-bodied and expression-bodied)
- Sync-async mappings for `ExecuteUpdate`/`ExecuteUpdateAsync` and `ExecuteDelete`/`ExecuteDeleteAsync`
- Materializer recognition for `AsEnumerable`, `Load`, `LoadAsync`, `ForEachAsync`, `ExecuteDelete`, `ExecuteDeleteAsync`, `ExecuteUpdate`, `ExecuteUpdateAsync`
- Edge case tests for LC001 (nested lambdas, method groups, expression-bodied members)
- Edge case tests for LC008 (nested async/sync contexts, ConfigureAwait, ValueTask)
- Edge case tests for LC011 (inheritance keys, composite keys, owned types)
- Async variant tests for LC002, LC003, LC005, LC009, LC015
- Cross-analyzer interaction tests (LC010+LC008, LC009+LC025)
- `.editorconfig` entries for LC017–LC030
- `CHANGELOG.md` with Keep a Changelog format
- README note about reserved rule IDs (LC019, LC022, LC024, LC027, LC028)
- CI: format verification (`dotnet format --verify-no-changes`)
- CI: NuGet package caching
- CI: coverage threshold check (75% minimum)

### Fixed
- `UnwrapConversions` null suppression bug — now falls back to original operation instead of returning null
- LC016 DateTime fixer: collision-avoiding variable names instead of hardcoded `"now"`
- LC030 fixer: handles block-bodied constructors (not just expression-bodied)
- LC030 fixer: preserves all variable declarators in multi-variable field declarations
- LC030 fixer: adds `using Microsoft.EntityFrameworkCore;` when inserting `IDbContextFactory<T>`
- README: broken LC030 section structure

### Changed
- **BREAKING**: CI workflow permissions scoped from `contents: write` to `contents: read` (write only on badge step)
- LC011 `EntityMissingPrimaryKeyAnalyzer`: scans source assembly only (not all referenced assemblies) for better performance
- LC002 `PrematureMaterializationAnalyzer`: refactored with extracted helper methods and early returns
- LC011 `EntityMissingPrimaryKeyAnalyzer`: refactored with `TryAddResolvedType` helper and flattened conditionals
- LC017 `WholeEntityProjectionAnalyzer`: refactored with extracted `TryExtractDbSetInfo` helper

## [2.21.0] - Initial Changelog Entry

### Analyzers

#### Performance
- LC002: Premature Materialization - early ToList/AsEnumerable before filtering
- LC003: Prefer Any() over Count() - using Count() > 0 instead of Any()
- LC005: Multiple OrderBy Calls - chained OrderBy() that cancel each other
- LC006: Cartesian Explosion Risk - multiple Include() without AsSplitQuery
- LC007: N+1 Looper - database queries inside loops
- LC010: SaveChanges Loop Tax - SaveChanges() inside a loop
- LC012: Optimize Bulk Delete - RemoveRange() instead of ExecuteDelete()
- LC015: Missing OrderBy Before Skip/Last - non-deterministic results
- LC017: Whole Entity Projection - loading entire entity when few properties accessed
- LC029: Redundant Identity Select - pointless Select(x => x) projection

#### Safety
- LC001: Local Method Smuggler - client-side evaluation from local methods
- LC004: Deferred Execution Leak - IQueryable passed to IEnumerable parameter
- LC008: Sync-over-Async - sync methods in async context
- LC013: Disposed Context Query - returning IQueryable from using-scoped context
- LC016: Avoid DateTime.Now in Queries - breaks query plan caching
- LC018: Avoid FromSqlRaw with Interpolation - SQL injection risk
- LC026: Missing CancellationToken - async operations without cancellation

#### Design
- LC009: The Tracking Tax - missing AsNoTracking() on read-only queries
- LC011: Entity Missing Primary Key - no Id, {Name}Id, [Key], or HasKey()
- LC014: Avoid String Case Conversion - ToLower()/ToUpper() prevents index usage
- LC020: String Contains with Comparison - missing StringComparison argument
- LC023: Find Instead of FirstOrDefault - using FirstOrDefault when Find is more efficient
- LC025: AsNoTracking with Update - conflicting tracking/update intent

#### Security
- LC021: Avoid IgnoreQueryFilters - bypassing critical global filters

#### Architecture
- LC030: DbContext in Singleton - potential lifetime mismatch

### Code Fixes
- LC001: Local Method Smuggler
- LC002: Premature Materialization
- LC003: Any Over Count
- LC005: Multiple OrderBy (ThenBy conversion)
- LC006: Cartesian Explosion (AsSplitQuery)
- LC008: Sync Blocker (async conversion)
- LC009: Missing AsNoTracking
- LC010: SaveChanges Loop Tax
- LC011: Entity Missing Primary Key
- LC012: Optimize RemoveRange
- LC014: Avoid String Case Conversion
- LC015: Missing OrderBy
- LC016: Avoid DateTime.Now (extract to variable)
- LC017: Whole Entity Projection (add Select)
- LC018: Avoid FromSqlRaw (use FromSqlInterpolated)
- LC020: String Contains with Comparison
- LC021: Avoid IgnoreQueryFilters
- LC023: Find Instead of FirstOrDefault
- LC025: AsNoTracking with Update
- LC026: Missing CancellationToken
- LC029: Redundant Identity Select
