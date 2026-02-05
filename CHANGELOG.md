# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
