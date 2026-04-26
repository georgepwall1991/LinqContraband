# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

### Added
- Added `docs/analyzer-layout-decision.md` to record the trade-offs, migration strategy, and long-term contract for the analyzer source layout

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
