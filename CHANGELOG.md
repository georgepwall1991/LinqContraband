# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
