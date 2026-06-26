---
layout: default
title: LinqContraband Rule Catalog
description: Full LinqContraband EF Core analyzer rule catalog grouped by query, materialization, loading, async, tracking, raw SQL, and schema design.
permalink: /rule-catalog.html
---

# Rule Catalog

The source of truth for rule metadata lives in `src/LinqContraband/Catalog/RuleCatalog.cs`.
This page is generated from that catalog and grouped by domain.

## Bulk Operations & Set-Based Writes

| Rule | Severity | Legacy Category | Fix | Docs | Sample |
| --- | --- | --- | --- | --- | --- |
| `LC012` Optimize: Use ExecuteDelete() instead of RemoveRange() | `Warning` | `Performance` | Code fix | [`LC012_OptimizeRemoveRange`](./LC012_OptimizeRemoveRange.html) | `Samples/LC012_OptimizeRemoveRange/` |
| `LC032` Use ExecuteUpdate for provable bulk scalar updates | `Info` | `Performance` | Code fix | [`LC032_ExecuteUpdateForBulkUpdates`](./LC032_ExecuteUpdateForBulkUpdates.html) | `Samples/LC032_ExecuteUpdateForBulkUpdates/` |
| `LC035` Missing Where before bulk execute | `Info` | `Safety` | Manual only | [`LC035_MissingWhereBeforeExecuteDeleteUpdate`](./LC035_MissingWhereBeforeExecuteDeleteUpdate.html) | `Samples/LC035_MissingWhereBeforeExecuteDeleteUpdate/` |

## Change Tracking & Context Lifetime

| Rule | Severity | Legacy Category | Fix | Docs | Sample |
| --- | --- | --- | --- | --- | --- |
| `LC009` Performance: Missing AsNoTracking() in Read-Only path | `Info` | `Performance` | Code fix | [`LC009_MissingAsNoTracking`](./LC009_MissingAsNoTracking.html) | `Samples/LC009_MissingAsNoTracking/` |
| `LC010` N+1 Write Problem: SaveChanges inside loop | `Warning` | `Performance` | Code fix | [`LC010_SaveChangesInLoop`](./LC010_SaveChangesInLoop.html) | `Samples/LC010_SaveChangesInLoop/` |
| `LC013` Disposed Context Query | `Warning` | `Reliability` | Manual only | [`LC013_DisposedContextQuery`](./LC013_DisposedContextQuery.html) | `Samples/LC013_DisposedContextQuery/` |
| `LC025` Avoid AsNoTracking with Update/Remove | `Warning` | `Reliability` | Code fix | [`LC025_AsNoTrackingWithUpdate`](./LC025_AsNoTrackingWithUpdate.html) | `Samples/LC025_AsNoTrackingWithUpdate/` |
| `LC030` Potential DbContext lifetime mismatch | `Info` | `Architecture` | Manual only | [`LC030_DbContextInSingleton`](./LC030_DbContextInSingleton.html) | `Samples/LC030_DbContextInSingleton/` |
| `LC039` Avoid repeated SaveChanges on the same context | `Info` | `Reliability` | Manual only | [`LC039_NestedSaveChanges`](./LC039_NestedSaveChanges.html) | `Samples/LC039_NestedSaveChanges/` |
| `LC040` Avoid mixing tracking modes on the same context | `Info` | `Reliability` | Manual only | [`LC040_MixedTrackingAndNoTracking`](./LC040_MixedTrackingAndNoTracking.html) | `Samples/LC040_MixedTrackingAndNoTracking/` |
| `LC044` AsNoTracking query mutated then SaveChanges — silent data loss | `Warning` | `Reliability` | Manual only | [`LC044_AsNoTrackingThenModify`](./LC044_AsNoTrackingThenModifySilentWrite.html) | `Samples/LC044_AsNoTrackingThenModify/` |

## Execution & Async

| Rule | Severity | Legacy Category | Fix | Docs | Sample |
| --- | --- | --- | --- | --- | --- |
| `LC007` N+1 Problem: Database execution inside loop | `Warning` | `Performance` | Code fix | [`LC007_NPlusOneLooper`](./LC007_NPlusOneLooper.html) | `Samples/LC007_NPlusOneLooper/` |
| `LC008` Sync-over-Async: Synchronous EF Core method in Async context | `Warning` | `Performance` | Code fix | [`LC008_SyncBlocker`](./LC008_SyncBlocker.html) | `Samples/LC008_SyncBlocker/` |
| `LC026` Missing CancellationToken in async call | `Info` | `Reliability` | Code fix | [`LC026_MissingCancellationToken`](./LC026_MissingCancellationToken.html) | `Samples/LC026_MissingCancellationToken/` |
| `LC036` DbContext captured by thread work item | `Warning` | `Safety` | Manual only | [`LC036_DbContextCapturedAcrossThreads`](./LC036_DbContextCapturedAcrossThreads.html) | `Samples/LC036_DbContextCapturedAcrossThreads/` |
| `LC043` Prefer await foreach over buffering async streams | `Info` | `Performance` | Code fix | [`LC043_AsyncEnumerableBuffering`](./LC043_AsyncEnumerableBuffering.html) | `Samples/LC043_AsyncEnumerableBuffering/` |

## Loading & Includes

| Rule | Severity | Legacy Category | Fix | Docs | Sample |
| --- | --- | --- | --- | --- | --- |
| `LC006` Cartesian Explosion Risk: Multiple Collection Includes | `Warning` | `Performance` | Code fix | [`LC006_CartesianExplosion`](./LC006_CartesianExplosion.html) | `Samples/LC006_CartesianExplosion/` |
| `LC019` Conditional Include Expression | `Warning` | `Correctness` | Manual only | [`LC019_ConditionalInclude`](./LC019_ConditionalInclude.html) | `Samples/LC019_ConditionalInclude/` |
| `LC028` Deep ThenInclude Chain | `Warning` | `Performance` | Manual only | [`LC028_DeepThenInclude`](./LC028_DeepThenInclude.html) | `Samples/LC028_DeepThenInclude/` |
| `LC038` Avoid excessive eager loading | `Info` | `Performance` | Manual only | [`LC038_ExcessiveEagerLoading`](./LC038_ExcessiveEagerLoading.html) | `Samples/LC038_ExcessiveEagerLoading/` |
| `LC042` Complex query should be tagged | `Info` | `Performance` | Manual only | [`LC042_MissingQueryTags`](./LC042_MissingQueryTags.html) | `Samples/LC042_MissingQueryTags/` |
| `LC045` Missing Include: navigation accessed on materialized entity | `Warning` | `Reliability` | Code fix | [`LC045_MissingInclude`](./LC045_MissingInclude.html) | `Samples/LC045_MissingInclude/` |

## Materialization & Projection

| Rule | Severity | Legacy Category | Fix | Docs | Sample |
| --- | --- | --- | --- | --- | --- |
| `LC002` Premature query continuation after materialization | `Warning` | `Performance` | Code fix | [`LC002_PrematureMaterialization`](./LC002_PrematureMaterialization.html) | `Samples/LC002_PrematureMaterialization/` |
| `LC003` Prefer Any() over Count() existence checks | `Warning` | `Performance` | Code fix | [`LC003_AnyOverCount`](./LC003_AnyOverCount.html) | `Samples/LC003_AnyOverCount/` |
| `LC017` Performance: Consider using Select() projection | `Info` | `Performance` | Code fix | [`LC017_WholeEntityProjection`](./LC017_WholeEntityProjection.html) | `Samples/LC017_WholeEntityProjection/` |
| `LC022` Nested collection materialization inside projection | `Info` | `Performance` | Code fix | [`LC022_ToListInSelectProjection`](./LC022_ToListInSelectProjection.html) | `Samples/LC022_ToListInSelectProjection/` |
| `LC023` Use Find/FindAsync for primary key lookups | `Info` | `Performance` | Code fix | [`LC023_FindInsteadOfFirstOrDefault`](./LC023_FindInsteadOfFirstOrDefault.html) | `Samples/LC023_FindInsteadOfFirstOrDefault/` |
| `LC029` Redundant identity Select | `Info` | `Performance` | Code fix | [`LC029_RedundantIdentitySelect`](./LC029_RedundantIdentitySelect.html) | `Samples/LC029_RedundantIdentitySelect/` |
| `LC031` Unbounded Query Materialization | `Info` | `Performance` | Manual only | [`LC031_UnboundedQueryMaterialization`](./LC031_UnboundedQueryMaterialization.html) | `Samples/LC031_UnboundedQueryMaterialization/` |
| `LC033` Use FrozenSet for provably read-only membership caches | `Info` | `Performance` | Code fix | [`LC033_UseFrozenSetForStaticMembershipCaches`](./LC033_UseFrozenSetForStaticMembershipCaches.html) | `Samples/LC033_UseFrozenSetForStaticMembershipCaches/` |
| `LC041` Single entity query over-fetches one consumed property | `Info` | `Performance` | Code fix | [`LC041_SingleEntityScalarProjection`](./LC041_SingleEntityScalarProjection.html) | `Samples/LC041_SingleEntityScalarProjection/` |

## Query Shape & Translation

| Rule | Severity | Legacy Category | Fix | Docs | Sample |
| --- | --- | --- | --- | --- | --- |
| `LC001` Client-side evaluation risk: Local method usage in IQueryable | `Warning` | `Performance` | Code fix | [`LC001_LocalMethod`](./LC001_LocalMethod.html) | `Samples/LC001_LocalMethod/` |
| `LC004` Deferred Execution Leak: IQueryable passed as IEnumerable | `Warning` | `Performance` | Code fix | [`LC004_IQueryableLeak`](./LC004_IQueryableLeak.html) | `Samples/LC004_IQueryableLeak/` |
| `LC005` Multiple OrderBy calls | `Warning` | `Performance` | Code fix | [`LC005_MultipleOrderBy`](./LC005_MultipleOrderBy.html) | `Samples/LC005_MultipleOrderBy/` |
| `LC014` Avoid String.ToLower() or ToUpper() in LINQ queries | `Warning` | `Performance` | Manual only | [`LC014_AvoidStringCaseConversion`](./LC014_AvoidStringCaseConversion.html) | `Samples/LC014_AvoidStringCaseConversion/` |
| `LC015` Deterministic Pagination: OrderBy required before Skip/Take | `Warning` | `Reliability` | Code fix | [`LC015_MissingOrderBy`](./LC015_MissingOrderBy.html) | `Samples/LC015_MissingOrderBy/` |
| `LC016` Avoid DateTime.Now/UtcNow in LINQ queries | `Warning` | `Performance` | Code fix | [`LC016_AvoidDateTimeNow`](./LC016_AvoidDateTimeNow.html) | `Samples/LC016_AvoidDateTimeNow/` |
| `LC020` Avoid untranslatable string comparison overloads | `Warning` | `Performance` | Code fix | [`LC020_StringContainsWithComparison`](./LC020_StringContainsWithComparison.html) | `Samples/LC020_StringContainsWithComparison/` |
| `LC024` GroupBy with Non-Translatable Projection | `Warning` | `Performance` | Manual only | [`LC024_GroupByNonTranslatable`](./LC024_GroupByNonTranslatable.html) | `Samples/LC024_GroupByNonTranslatable/` |

## Raw SQL & Security

| Rule | Severity | Legacy Category | Fix | Docs | Sample |
| --- | --- | --- | --- | --- | --- |
| `LC018` Avoid FromSqlRaw with interpolated strings | `Warning` | `Security` | Code fix | [`LC018_AvoidFromSqlRawWithInterpolation`](./LC018_AvoidFromSqlRawWithInterpolation.html) | `Samples/LC018_AvoidFromSqlRawWithInterpolation/` |
| `LC021` Avoid IgnoreQueryFilters | `Warning` | `Security` | Code fix | [`LC021_AvoidIgnoreQueryFilters`](./LC021_AvoidIgnoreQueryFilters.html) | `Samples/LC021_AvoidIgnoreQueryFilters/` |
| `LC034` Avoid ExecuteSqlRaw with interpolated strings | `Warning` | `Security` | Code fix | [`LC034_AvoidExecuteSqlRawWithInterpolation`](./LC034_AvoidExecuteSqlRawWithInterpolation.html) | `Samples/LC034_AvoidExecuteSqlRawWithInterpolation/` |
| `LC037` Avoid constructed raw SQL strings | `Warning` | `Security` | Manual only | [`LC037_RawSqlStringConstruction`](./LC037_RawSqlStringConstruction.html) | `Samples/LC037_RawSqlStringConstruction/` |

## Schema & Modeling

| Rule | Severity | Legacy Category | Fix | Docs | Sample |
| --- | --- | --- | --- | --- | --- |
| `LC011` Design: Entity missing Primary Key | `Warning` | `Design` | Code fix | [`LC011_EntityMissingPrimaryKey`](./LC011_EntityMissingPrimaryKey.html) | `Samples/LC011_EntityMissingPrimaryKey/` |
| `LC027` Missing Explicit Foreign Key Property | `Info` | `Design` | Code fix | [`LC027_MissingExplicitForeignKey`](./LC027_MissingExplicitForeignKey.html) | `Samples/LC027_MissingExplicitForeignKey/` |

