# Rule Catalog

The source of truth for rule metadata lives in `src/LinqContraband/Catalog/RuleCatalog.cs`.
This page is generated from that catalog and grouped by domain.

## Bulk Operations & Set-Based Writes

| Rule | Severity | Legacy Category | Fix | Docs | Sample |
| --- | --- | --- | --- | --- | --- |
| `LC012` Optimize: Use ExecuteDelete() instead of RemoveRange() | `Warning` | `Performance` | Code fix | [`LC012_OptimizeRemoveRange`](./LC012_OptimizeRemoveRange.md) | `Samples/LC012_OptimizeRemoveRange/` |
| `LC032` Use ExecuteUpdate for provable bulk scalar updates | `Info` | `Performance` | Manual only | [`LC032_ExecuteUpdateForBulkUpdates`](./LC032_ExecuteUpdateForBulkUpdates.md) | `Samples/LC032_ExecuteUpdateForBulkUpdates/` |
| `LC035` Missing Where before bulk execute | `Info` | `Safety` | Manual only | [`LC035_MissingWhereBeforeExecuteDeleteUpdate`](./LC035_MissingWhereBeforeExecuteDeleteUpdate.md) | `Samples/LC035_MissingWhereBeforeExecuteDeleteUpdate/` |

## Change Tracking & Context Lifetime

| Rule | Severity | Legacy Category | Fix | Docs | Sample |
| --- | --- | --- | --- | --- | --- |
| `LC009` Performance: Missing AsNoTracking() in Read-Only path | `Info` | `Performance` | Code fix | [`LC009_MissingAsNoTracking`](./LC009_MissingAsNoTracking.md) | `Samples/LC009_MissingAsNoTracking/` |
| `LC010` N+1 Write Problem: SaveChanges inside loop | `Warning` | `Performance` | Code fix | [`LC010_SaveChangesInLoop`](./LC010_SaveChangesInLoop.md) | `Samples/LC010_SaveChangesInLoop/` |
| `LC013` Disposed Context Query | `Warning` | `Reliability` | Manual only | [`LC013_DisposedContextQuery`](./LC013_DisposedContextQuery.md) | `Samples/LC013_DisposedContextQuery/` |
| `LC025` Avoid AsNoTracking with Update/Remove | `Warning` | `Reliability` | Code fix | [`LC025_AsNoTrackingWithUpdate`](./LC025_AsNoTrackingWithUpdate.md) | `Samples/LC025_AsNoTrackingWithUpdate/` |
| `LC030` Potential DbContext lifetime mismatch | `Info` | `Architecture` | Manual only | [`LC030_DbContextInSingleton`](./LC030_DbContextInSingleton.md) | `Samples/LC030_DbContextInSingleton/` |
| `LC039` Avoid repeated SaveChanges on the same context | `Info` | `Reliability` | Manual only | [`LC039_NestedSaveChanges`](./LC039_NestedSaveChanges.md) | `Samples/LC039_NestedSaveChanges/` |
| `LC040` Avoid mixing tracking modes on the same context | `Info` | `Reliability` | Manual only | [`LC040_MixedTrackingAndNoTracking`](./LC040_MixedTrackingAndNoTracking.md) | `Samples/LC040_MixedTrackingAndNoTracking/` |
| `LC044` AsNoTracking query mutated then SaveChanges — silent data loss | `Warning` | `Reliability` | Manual only | [`LC044_AsNoTrackingThenModify`](./LC044_AsNoTrackingThenModifySilentWrite.md) | `Samples/LC044_AsNoTrackingThenModify/` |

## Execution & Async

| Rule | Severity | Legacy Category | Fix | Docs | Sample |
| --- | --- | --- | --- | --- | --- |
| `LC007` N+1 Problem: Database execution inside loop | `Warning` | `Performance` | Code fix | [`LC007_NPlusOneLooper`](./LC007_NPlusOneLooper.md) | `Samples/LC007_NPlusOneLooper/` |
| `LC008` Sync-over-Async: Synchronous EF Core method in Async context | `Warning` | `Performance` | Code fix | [`LC008_SyncBlocker`](./LC008_SyncBlocker.md) | `Samples/LC008_SyncBlocker/` |
| `LC026` Missing CancellationToken in async call | `Info` | `Reliability` | Code fix | [`LC026_MissingCancellationToken`](./LC026_MissingCancellationToken.md) | `Samples/LC026_MissingCancellationToken/` |
| `LC036` DbContext captured by thread work item | `Warning` | `Safety` | Manual only | [`LC036_DbContextCapturedAcrossThreads`](./LC036_DbContextCapturedAcrossThreads.md) | `Samples/LC036_DbContextCapturedAcrossThreads/` |
| `LC043` Prefer await foreach over buffering async streams | `Info` | `Performance` | Code fix | [`LC043_AsyncEnumerableBuffering`](./LC043_AsyncEnumerableBuffering.md) | `Samples/LC043_AsyncEnumerableBuffering/` |

## Loading & Includes

| Rule | Severity | Legacy Category | Fix | Docs | Sample |
| --- | --- | --- | --- | --- | --- |
| `LC006` Cartesian Explosion Risk: Multiple Collection Includes | `Warning` | `Performance` | Code fix | [`LC006_CartesianExplosion`](./LC006_CartesianExplosion.md) | `Samples/LC006_CartesianExplosion/` |
| `LC019` Conditional Include Expression | `Warning` | `Correctness` | Manual only | [`LC019_ConditionalInclude`](./LC019_ConditionalInclude.md) | `Samples/LC019_ConditionalInclude/` |
| `LC028` Deep ThenInclude Chain | `Warning` | `Performance` | Manual only | [`LC028_DeepThenInclude`](./LC028_DeepThenInclude.md) | `Samples/LC028_DeepThenInclude/` |
| `LC038` Avoid excessive eager loading | `Info` | `Performance` | Manual only | [`LC038_ExcessiveEagerLoading`](./LC038_ExcessiveEagerLoading.md) | `Samples/LC038_ExcessiveEagerLoading/` |
| `LC042` Complex query should be tagged | `Info` | `Performance` | Manual only | [`LC042_MissingQueryTags`](./LC042_MissingQueryTags.md) | `Samples/LC042_MissingQueryTags/` |

## Materialization & Projection

| Rule | Severity | Legacy Category | Fix | Docs | Sample |
| --- | --- | --- | --- | --- | --- |
| `LC002` Premature query continuation after materialization | `Warning` | `Performance` | Code fix | [`LC002_PrematureMaterialization`](./LC002_PrematureMaterialization.md) | `Samples/LC002_PrematureMaterialization/` |
| `LC003` Prefer Any() over Count() existence checks | `Warning` | `Performance` | Code fix | [`LC003_AnyOverCount`](./LC003_AnyOverCount.md) | `Samples/LC003_AnyOverCount/` |
| `LC017` Performance: Consider using Select() projection | `Info` | `Performance` | Code fix | [`LC017_WholeEntityProjection`](./LC017_WholeEntityProjection.md) | `Samples/LC017_WholeEntityProjection/` |
| `LC022` Nested collection materialization inside projection | `Info` | `Performance` | Code fix | [`LC022_ToListInSelectProjection`](./LC022_ToListInSelectProjection.md) | `Samples/LC022_ToListInSelectProjection/` |
| `LC023` Use Find/FindAsync for primary key lookups | `Info` | `Performance` | Code fix | [`LC023_FindInsteadOfFirstOrDefault`](./LC023_FindInsteadOfFirstOrDefault.md) | `Samples/LC023_FindInsteadOfFirstOrDefault/` |
| `LC029` Redundant identity Select | `Info` | `Performance` | Code fix | [`LC029_RedundantIdentitySelect`](./LC029_RedundantIdentitySelect.md) | `Samples/LC029_RedundantIdentitySelect/` |
| `LC031` Unbounded Query Materialization | `Info` | `Performance` | Manual only | [`LC031_UnboundedQueryMaterialization`](./LC031_UnboundedQueryMaterialization.md) | `Samples/LC031_UnboundedQueryMaterialization/` |
| `LC033` Use FrozenSet for provably read-only membership caches | `Info` | `Performance` | Code fix | [`LC033_UseFrozenSetForStaticMembershipCaches`](./LC033_UseFrozenSetForStaticMembershipCaches.md) | `Samples/LC033_UseFrozenSetForStaticMembershipCaches/` |
| `LC041` Single entity query over-fetches one consumed property | `Info` | `Performance` | Code fix | [`LC041_SingleEntityScalarProjection`](./LC041_SingleEntityScalarProjection.md) | `Samples/LC041_SingleEntityScalarProjection/` |

## Query Shape & Translation

| Rule | Severity | Legacy Category | Fix | Docs | Sample |
| --- | --- | --- | --- | --- | --- |
| `LC001` Client-side evaluation risk: Local method usage in IQueryable | `Warning` | `Performance` | Code fix | [`LC001_LocalMethod`](./LC001_LocalMethod.md) | `Samples/LC001_LocalMethod/` |
| `LC004` Deferred Execution Leak: IQueryable passed as IEnumerable | `Warning` | `Performance` | Code fix | [`LC004_IQueryableLeak`](./LC004_IQueryableLeak.md) | `Samples/LC004_IQueryableLeak/` |
| `LC005` Multiple OrderBy calls | `Warning` | `Performance` | Code fix | [`LC005_MultipleOrderBy`](./LC005_MultipleOrderBy.md) | `Samples/LC005_MultipleOrderBy/` |
| `LC014` Avoid String.ToLower() or ToUpper() in LINQ queries | `Warning` | `Performance` | Manual only | [`LC014_AvoidStringCaseConversion`](./LC014_AvoidStringCaseConversion.md) | `Samples/LC014_AvoidStringCaseConversion/` |
| `LC015` Deterministic Pagination: OrderBy required before Skip/Take | `Warning` | `Reliability` | Code fix | [`LC015_MissingOrderBy`](./LC015_MissingOrderBy.md) | `Samples/LC015_MissingOrderBy/` |
| `LC016` Avoid DateTime.Now/UtcNow in LINQ queries | `Warning` | `Performance` | Code fix | [`LC016_AvoidDateTimeNow`](./LC016_AvoidDateTimeNow.md) | `Samples/LC016_AvoidDateTimeNow/` |
| `LC020` Avoid untranslatable string comparison overloads | `Warning` | `Performance` | Code fix | [`LC020_StringContainsWithComparison`](./LC020_StringContainsWithComparison.md) | `Samples/LC020_StringContainsWithComparison/` |
| `LC024` GroupBy with Non-Translatable Projection | `Warning` | `Performance` | Manual only | [`LC024_GroupByNonTranslatable`](./LC024_GroupByNonTranslatable.md) | `Samples/LC024_GroupByNonTranslatable/` |

## Raw SQL & Security

| Rule | Severity | Legacy Category | Fix | Docs | Sample |
| --- | --- | --- | --- | --- | --- |
| `LC018` Avoid FromSqlRaw with interpolated strings | `Warning` | `Security` | Code fix | [`LC018_AvoidFromSqlRawWithInterpolation`](./LC018_AvoidFromSqlRawWithInterpolation.md) | `Samples/LC018_AvoidFromSqlRawWithInterpolation/` |
| `LC021` Avoid IgnoreQueryFilters | `Warning` | `Security` | Code fix | [`LC021_AvoidIgnoreQueryFilters`](./LC021_AvoidIgnoreQueryFilters.md) | `Samples/LC021_AvoidIgnoreQueryFilters/` |
| `LC034` Avoid ExecuteSqlRaw with interpolated strings | `Warning` | `Security` | Code fix | [`LC034_AvoidExecuteSqlRawWithInterpolation`](./LC034_AvoidExecuteSqlRawWithInterpolation.md) | `Samples/LC034_AvoidExecuteSqlRawWithInterpolation/` |
| `LC037` Avoid constructed raw SQL strings | `Warning` | `Security` | Manual only | [`LC037_RawSqlStringConstruction`](./LC037_RawSqlStringConstruction.md) | `Samples/LC037_RawSqlStringConstruction/` |

## Schema & Modeling

| Rule | Severity | Legacy Category | Fix | Docs | Sample |
| --- | --- | --- | --- | --- | --- |
| `LC011` Design: Entity missing Primary Key | `Warning` | `Design` | Code fix | [`LC011_EntityMissingPrimaryKey`](./LC011_EntityMissingPrimaryKey.md) | `Samples/LC011_EntityMissingPrimaryKey/` |
| `LC027` Missing Explicit Foreign Key Property | `Info` | `Design` | Code fix | [`LC027_MissingExplicitForeignKey`](./LC027_MissingExplicitForeignKey.md) | `Samples/LC027_MissingExplicitForeignKey/` |

