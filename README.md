# LinqContraband

<div align="center">

![LinqContraband icon — EF Core LINQ performance Roslyn analyzer](https://raw.githubusercontent.com/georgepwall1991/LinqContraband/master/icon.png)

### Stop Smuggling Bad Queries into Production

[![NuGet](https://img.shields.io/nuget/v/LinqContraband.svg)](https://www.nuget.org/packages/LinqContraband)
[![Downloads](https://img.shields.io/nuget/dt/LinqContraband.svg)](https://www.nuget.org/packages/LinqContraband)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](https://github.com/georgepwall1991/LinqContraband/blob/master/LICENSE)
[![Build](https://img.shields.io/github/actions/workflow/status/georgepwall1991/LinqContraband/dotnet.yml?label=build)](https://github.com/georgepwall1991/LinqContraband/actions/workflows/dotnet.yml)
[![Coverage](https://raw.githubusercontent.com/georgepwall1991/LinqContraband/master/.github/badges/coverage.svg)](https://github.com/georgepwall1991/LinqContraband/actions/workflows/dotnet.yml)
[![OpenSSF Scorecard](https://img.shields.io/ossf-scorecard/github.com/georgepwall1991/LinqContraband?label=openssf%20scorecard)](https://scorecard.dev/viewer/?uri=github.com/georgepwall1991/LinqContraband)

</div>

**Compile-time EF Core LINQ performance analyzer for .NET** — a high-signal Roslyn analyzer that catches N+1 queries, client-side evaluation, premature materialization, sync-over-async, missing AsNoTracking, raw SQL injection risks, and other DbContext query issues in the editor and CI—not production.

## The problem

Entity Framework Core and LINQ compile cleanly even when the query shape will hurt production. An N+1 `Find` inside a loop, `ToList()` before `Where`, a local method that forces client-side evaluation, sync-over-async on `DbContext`, or `FromSqlRaw($"...")` only fail under load, at 3 AM, or as a security incident.

Runtime profilers and code review miss what static analysis can prove from your `IQueryable` chains and EF Core API usage.

## What it catches

LinqContraband reports proven EF Core LINQ and DbContext pitfalls early:

- N+1 database execution inside loops (`Find`, materializers, explicit load)
- premature materialization (`ToList`/`AsEnumerable` before filters)
- client-side evaluation risk from non-translatable local methods
- sync-over-async EF Core calls in async methods
- missing or misused AsNoTracking (tracking tax, silent writes, mixed modes)
- Cartesian explosion and missing/excessive Include paths
- SaveChanges inside loops and nested SaveChanges
- raw SQL injection patterns (`FromSqlRaw` / `ExecuteSqlRaw` interpolation)
- DbContext lifetime and concurrent same-context operations
- unbounded materialization, projection waste, and pagination without OrderBy

When the analyzer cannot prove an EF-backed query shape statically, it **stays quiet**. High-signal feedback, not noisy guesses.

## Install

```bash
dotnet add package LinqContraband
```

That adds the latest release. To edit the project file by hand instead, use the version shown on the [NuGet badge](https://www.nuget.org/packages/LinqContraband):

```xml
<PackageReference Include="LinqContraband" Version="x.y.z" PrivateAssets="all" />
```

**No runtime dependency** is added to your app. LinqContraband runs as a Roslyn analyzer during build and in supported IDEs (Visual Studio, Rider, VS Code / C# Dev Kit) and CI.

Install only from NuGet or from this repository. LinqContraband is not distributed as a standalone ZIP installer or executable; treat third-party ZIP downloads as untrusted.

- **Official package:** [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- **Canonical source:** [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- **Documentation:** [georgepwall1991.github.io/LinqContraband](https://georgepwall1991.github.io/LinqContraband/)

## A taste

```csharp
// LC002: ToList() pulls every order into memory, then filters in C#.
var slow = db.Orders.ToList().Where(o => o.DueDate < today);

// Fix (offered as a code fix): filter in SQL, then materialize.
var fast = db.Orders.Where(o => o.DueDate < today).ToList();
```

```csharp
// LC007: one database round trip per customer (N+1).
foreach (var id in customerIds)
    customers.Add(db.Customers.Find(id));

// Fix: one query for the whole set.
customers = db.Customers.Where(c => customerIds.Contains(c.Id)).ToList();
```

Every diagnostic's help link in your IDE opens the rule's page, which shows the problem, the fix, and the cases the rule
deliberately leaves alone.

## See it work

Product-flow diagrams from real sample diagnostics and shipped LC message formats:

### 1. Build / IDE diagnostics (EF Core LINQ)

![LinqContraband Roslyn analyzer warnings for EF Core LINQ performance — LC001 client-side evaluation, LC002 premature materialization, LC007 N+1, LC018 FromSqlRaw SQL injection](https://raw.githubusercontent.com/georgepwall1991/LinqContraband/master/assets/flow-ide-diagnostics.svg)

### 2. Before / after code fix (premature materialization)

![Before and after: EF Core ToList before Where fixed to filter then materialize with LC002 premature materialization code fix](https://raw.githubusercontent.com/georgepwall1991/LinqContraband/master/assets/flow-before-after-fix.svg)

### 3. Product loop — analyzer in IDE and CI

![LinqContraband product loop: Roslyn analyzer build diagnostics for DbContext and IQueryable in the IDE and ContinuousIntegrationBuild CI](https://raw.githubusercontent.com/georgepwall1991/LinqContraband/master/assets/flow-analyzer-ci-loop.svg)

## 30-second path

1. Reference the package with `PrivateAssets="all"`.
2. Keep writing EF Core LINQ as usual (`DbSet`, `IQueryable`, `Include`, `SaveChanges`).
3. Build in the IDE or with `ContinuousIntegrationBuild=true` on the command line so analyzers run.
4. Fix any `LC00x` warnings (many have code fixes).
5. Optionally promote critical rules to error in `.editorconfig` (see Configuration below).

## Feature snapshot

| Area | What LinqContraband does |
|------|--------------------------|
| N+1 queries | Flags database execution inside loops and SaveChanges-in-loop write amplification. |
| Materialization | Catches premature `ToList`/`AsEnumerable` and redundant second materializers. |
| Translation | Reports local methods and non-translatable string/date patterns that risk client-side evaluation. |
| Tracking | Guides AsNoTracking, silent-write, and mixed tracking-mode hazards. |
| Loading | Detects Cartesian explosion, missing Include, deep ThenInclude, excessive eager loading. |
| Async | Sync-over-async, missing CancellationToken, async stream buffering, concurrent DbContext use. |
| Raw SQL | Interpolated `FromSqlRaw`/`ExecuteSqlRaw` and constructed SQL string risks. |
| Modeling | Missing primary keys and explicit foreign-key properties when statically provable. |

## Compatibility

- **.NET / Roslyn hosts:** Visual Studio, Rider, VS Code (C# Dev Kit), and `dotnet build` / CI
- **EF Core:** Modern Entity Framework Core versions used with C# `IQueryable` / `DbContext` APIs
- **Package kind:** Development dependency analyzer (`PrivateAssets="all"`); no app runtime package

## Rules

<!-- rule-table:start (generated from RuleCatalog by tools/RuleCatalogDocGenerator; do not edit by hand) -->

**50 rules**, 33 with automatic code fixes. Each rule links to its full page: what it flags, why it matters, how to fix it, and where it deliberately stays quiet.

| Rule | What it catches | Default severity | Code fix |
| --- | --- | --- | --- |
| [LC001](https://georgepwall1991.github.io/LinqContraband/LC001_LocalMethod.html) | Client-side evaluation risk: Local method usage in IQueryable | Warning | Yes |
| [LC002](https://georgepwall1991.github.io/LinqContraband/LC002_PrematureMaterialization.html) | Premature query continuation after materialization | Warning | Yes |
| [LC003](https://georgepwall1991.github.io/LinqContraband/LC003_AnyOverCount.html) | Prefer Any() over Count() existence checks | Warning | Yes |
| [LC004](https://georgepwall1991.github.io/LinqContraband/LC004_IQueryableLeak.html) | Deferred Execution Leak: IQueryable passed as IEnumerable | Warning | Yes |
| [LC005](https://georgepwall1991.github.io/LinqContraband/LC005_MultipleOrderBy.html) | Multiple OrderBy calls | Warning | Yes |
| [LC006](https://georgepwall1991.github.io/LinqContraband/LC006_CartesianExplosion.html) | Cartesian Explosion Risk: Multiple Collection Includes | Warning | Yes |
| [LC007](https://georgepwall1991.github.io/LinqContraband/LC007_NPlusOneLooper.html) | N+1 Problem: Database execution inside loop | Warning | Yes |
| [LC008](https://georgepwall1991.github.io/LinqContraband/LC008_SyncBlocker.html) | Sync-over-Async: Synchronous EF Core method in Async context | Warning | Yes |
| [LC009](https://georgepwall1991.github.io/LinqContraband/LC009_MissingAsNoTracking.html) | Performance: Missing AsNoTracking() in Read-Only path | Info | Yes |
| [LC010](https://georgepwall1991.github.io/LinqContraband/LC010_SaveChangesInLoop.html) | N+1 Write Problem: SaveChanges inside loop | Warning | Yes |
| [LC011](https://georgepwall1991.github.io/LinqContraband/LC011_EntityMissingPrimaryKey.html) | Design: Entity missing Primary Key | Warning | Yes |
| [LC012](https://georgepwall1991.github.io/LinqContraband/LC012_OptimizeRemoveRange.html) | Optimize: Use ExecuteDelete() instead of RemoveRange() | Warning | Yes |
| [LC013](https://georgepwall1991.github.io/LinqContraband/LC013_DisposedContextQuery.html) | Disposed Context Query | Warning | Manual |
| [LC014](https://georgepwall1991.github.io/LinqContraband/LC014_AvoidStringCaseConversion.html) | Avoid String.ToLower() or ToUpper() in LINQ queries | Warning | Manual |
| [LC015](https://georgepwall1991.github.io/LinqContraband/LC015_MissingOrderBy.html) | Deterministic Pagination: OrderBy required before Skip/Take | Warning | Yes |
| [LC016](https://georgepwall1991.github.io/LinqContraband/LC016_AvoidDateTimeNow.html) | Avoid DateTime.Now/UtcNow in LINQ queries | Warning | Yes |
| [LC017](https://georgepwall1991.github.io/LinqContraband/LC017_WholeEntityProjection.html) | Performance: Consider using Select() projection | Info | Yes |
| [LC018](https://georgepwall1991.github.io/LinqContraband/LC018_AvoidFromSqlRawWithInterpolation.html) | Avoid FromSqlRaw with interpolated strings | Warning | Yes |
| [LC019](https://georgepwall1991.github.io/LinqContraband/LC019_ConditionalInclude.html) | Conditional Include Expression | Warning | Manual |
| [LC020](https://georgepwall1991.github.io/LinqContraband/LC020_StringContainsWithComparison.html) | Avoid untranslatable string comparison overloads | Warning | Yes |
| [LC021](https://georgepwall1991.github.io/LinqContraband/LC021_AvoidIgnoreQueryFilters.html) | Avoid IgnoreQueryFilters | Warning | Yes |
| [LC022](https://georgepwall1991.github.io/LinqContraband/LC022_ToListInSelectProjection.html) | Nested collection materialization inside projection | Info | Yes |
| [LC023](https://georgepwall1991.github.io/LinqContraband/LC023_FindInsteadOfFirstOrDefault.html) | Use Find/FindAsync for primary key lookups | Info | Yes |
| [LC024](https://georgepwall1991.github.io/LinqContraband/LC024_GroupByNonTranslatable.html) | GroupBy with Non-Translatable Projection | Warning | Manual |
| [LC025](https://georgepwall1991.github.io/LinqContraband/LC025_AsNoTrackingWithUpdate.html) | Avoid AsNoTracking with Update/Remove | Warning | Yes |
| [LC026](https://georgepwall1991.github.io/LinqContraband/LC026_MissingCancellationToken.html) | Missing CancellationToken in async call | Info | Yes |
| [LC027](https://georgepwall1991.github.io/LinqContraband/LC027_MissingExplicitForeignKey.html) | Missing Explicit Foreign Key Property | Info | Yes |
| [LC028](https://georgepwall1991.github.io/LinqContraband/LC028_DeepThenInclude.html) | Deep ThenInclude Chain | Warning | Manual |
| [LC029](https://georgepwall1991.github.io/LinqContraband/LC029_RedundantIdentitySelect.html) | Redundant identity Select | Info | Yes |
| [LC030](https://georgepwall1991.github.io/LinqContraband/LC030_DbContextInSingleton.html) | Potential DbContext lifetime mismatch | Info | Manual |
| [LC031](https://georgepwall1991.github.io/LinqContraband/LC031_UnboundedQueryMaterialization.html) | Unbounded Query Materialization | Info | Manual |
| [LC032](https://georgepwall1991.github.io/LinqContraband/LC032_ExecuteUpdateForBulkUpdates.html) | Use ExecuteUpdate for provable bulk scalar updates | Info | Yes |
| [LC033](https://georgepwall1991.github.io/LinqContraband/LC033_UseFrozenSetForStaticMembershipCaches.html) | Use FrozenSet for provably read-only membership caches | Info | Yes |
| [LC034](https://georgepwall1991.github.io/LinqContraband/LC034_AvoidExecuteSqlRawWithInterpolation.html) | Avoid ExecuteSqlRaw with interpolated strings | Warning | Yes |
| [LC035](https://georgepwall1991.github.io/LinqContraband/LC035_MissingWhereBeforeExecuteDeleteUpdate.html) | Missing Where before bulk execute | Info | Manual |
| [LC036](https://georgepwall1991.github.io/LinqContraband/LC036_DbContextCapturedAcrossThreads.html) | DbContext captured by thread work item | Warning | Manual |
| [LC037](https://georgepwall1991.github.io/LinqContraband/LC037_RawSqlStringConstruction.html) | Avoid constructed raw SQL strings | Warning | Manual |
| [LC038](https://georgepwall1991.github.io/LinqContraband/LC038_ExcessiveEagerLoading.html) | Avoid excessive eager loading | Info | Manual |
| [LC039](https://georgepwall1991.github.io/LinqContraband/LC039_NestedSaveChanges.html) | Avoid repeated SaveChanges on the same context | Info | Manual |
| [LC040](https://georgepwall1991.github.io/LinqContraband/LC040_MixedTrackingAndNoTracking.html) | Avoid mixing tracking modes on the same context | Info | Manual |
| [LC041](https://georgepwall1991.github.io/LinqContraband/LC041_SingleEntityScalarProjection.html) | Single entity query over-fetches one consumed property | Info | Yes |
| [LC042](https://georgepwall1991.github.io/LinqContraband/LC042_MissingQueryTags.html) | Complex query should be tagged | Info | Manual |
| [LC043](https://georgepwall1991.github.io/LinqContraband/LC043_AsyncEnumerableBuffering.html) | Prefer await foreach over buffering async streams | Info | Yes |
| [LC044](https://georgepwall1991.github.io/LinqContraband/LC044_AsNoTrackingThenModifySilentWrite.html) | AsNoTracking query mutated then SaveChanges — silent data loss | Warning | Manual |
| [LC045](https://georgepwall1991.github.io/LinqContraband/LC045_MissingInclude.html) | Missing Include: navigation accessed on materialized entity | Warning | Yes |
| [LC046](https://georgepwall1991.github.io/LinqContraband/LC046_ConcurrentDbContextOperations.html) | Concurrent EF Core operations on the same DbContext | Warning | Manual |
| [LC047](https://georgepwall1991.github.io/LinqContraband/LC047_ExecuteDeleteBypassesTrackedDelete.html) | ExecuteDelete bypasses the tracked delete pipeline | Warning | Yes |
| [LC048](https://georgepwall1991.github.io/LinqContraband/LC048_LostUpdateRisk.html) | Tracked update can overwrite a concurrent change | Warning | Manual |
| [LC049](https://georgepwall1991.github.io/LinqContraband/LC049_IncludeIgnoredByProjection.html) | Include is ignored by a Select projection | Info | Yes |
| [LC050](https://georgepwall1991.github.io/LinqContraband/LC050_OrderByBeforeDistinct.html) | OrderBy before Distinct is discarded | Warning | Yes |

<!-- rule-table:end -->

Browse the same rules grouped by failure mode in the [rule catalog](https://georgepwall1991.github.io/LinqContraband/rule-catalog.html), or start from a topic guide:

- [N+1 query detector](https://georgepwall1991.github.io/LinqContraband/ef-core-n-plus-one-query-detector/) and [SaveChanges in loops](https://georgepwall1991.github.io/LinqContraband/ef-core-savechanges-in-loop-analyzer/)
- [Client-side evaluation](https://georgepwall1991.github.io/LinqContraband/ef-core-client-side-evaluation-analyzer/) and [premature materialization](https://georgepwall1991.github.io/LinqContraband/ef-core-premature-materialization-analyzer/)
- [Include and eager loading](https://georgepwall1991.github.io/LinqContraband/ef-core-include-analyzer/) and [projection](https://georgepwall1991.github.io/LinqContraband/ef-core-projection-analyzer/)
- [AsNoTracking and change tracking](https://georgepwall1991.github.io/LinqContraband/ef-core-asnotracking-analyzer/) and [DbContext lifetime](https://georgepwall1991.github.io/LinqContraband/ef-core-dbcontext-lifetime-analyzer/)
- [Async queries](https://georgepwall1991.github.io/LinqContraband/ef-core-async-query-analyzer/) and [CancellationToken](https://georgepwall1991.github.io/LinqContraband/ef-core-cancellation-token-analyzer/)
- [Raw SQL injection](https://georgepwall1991.github.io/LinqContraband/ef-core-raw-sql-injection-analyzer/), [ExecuteUpdate](https://georgepwall1991.github.io/LinqContraband/ef-core-executeupdate-analyzer/), and [pagination OrderBy](https://georgepwall1991.github.io/LinqContraband/ef-core-pagination-orderby-analyzer/)
- [EF Core query performance checklist](https://georgepwall1991.github.io/LinqContraband/ef-core-query-performance-checklist/)

## Configuration

Pick a preset with one line in your project file (or `Directory.Build.props`):

```xml
<PropertyGroup>
  <LinqContrabandPreset>security</LinqContrabandPreset>
</PropertyGroup>
```

| Preset | What it does |
| --- | --- |
| `security` | SQL injection rules (LC018, LC034, LC037) fail the build. |
| `critical` | `security` plus the runtime-failure and silent data-loss rules (LC013, LC019, LC036, LC044, LC046, LC047, LC048) fail the build. |
| `strict` | Every warning rule fails the build and every advisory rule becomes a warning. |
| `essentials` | Advisory (Info) rules are turned off; warning rules keep their defaults. |

Combine presets with `;`, for example `security;essentials`. Your own `.editorconfig` entries still win over a preset.

Or set any rule's severity yourself in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.LC001.severity = error
dotnet_diagnostic.LC002.severity = error
dotnet_diagnostic.LC003.severity = warning

# Optional rule-specific thresholds
dotnet_code_quality.LC038.include_threshold = 4
dotnet_code_quality.LC042.query_operator_threshold = 3
```

Advisory rules default to `Info`, so they show up as hints without drowning out the higher-confidence warnings.

## Run it in CI

LinqContraband runs inside the normal `dotnet build`, so a CI job needs no database, service container, or extra tool:

```yaml
- run: dotnet restore
- run: dotnet build --configuration Release --no-restore
```

Use the `security` or `critical` preset (see Configuration) to block pull requests on the rules that matter most, or
promote individual rules to `error` in `.editorconfig`. The
[CI guide](https://georgepwall1991.github.io/LinqContraband/ef-core-query-analyzer-ci/) covers a gradual rollout.

## Contributing

Found a new way to smuggle bad queries? [Open an issue](https://github.com/georgepwall1991/LinqContraband/issues) or
send a pull request. The [contributing guide](https://github.com/georgepwall1991/LinqContraband/blob/master/CONTRIBUTING.md)
explains how to add or change a rule.

License: [MIT](https://github.com/georgepwall1991/LinqContraband/blob/master/LICENSE)
