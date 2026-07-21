---
layout: default
title: LinqContraband Rule Catalog
description: Full LinqContraband EF Core analyzer rule catalog grouped by query, materialization, loading, async, tracking, raw SQL, and schema design.
permalink: /rule-catalog.html
body_class: page-rule-catalog
---

<section class="catalog-intro">
  <div class="catalog-intro__copy">
    <p>The source of truth for rule metadata lives in <code>src/LinqContraband/Catalog/RuleCatalog.cs</code>. This page is generated from that catalog and grouped by EF Core failure mode.</p>
  </div>
  <div class="metric-strip" aria-label="Rule catalog summary">
    <div class="metric"><strong>46</strong><span>rules</span></div>
    <div class="metric"><strong>28</strong><span>warnings</span></div>
    <div class="metric"><strong>30</strong><span>code fixes</span></div>
  </div>
</section>

<p class="eyebrow">8 diagnostic domains</p>

<section class="rule-domain" aria-labelledby="bulk-operations-set-based-writes">
  <div class="rule-domain__heading">
    <h2 id="bulk-operations-set-based-writes">Bulk Operations &amp; Set-Based Writes</h2>
    <p>Keep destructive and high-volume writes set-based while making the risky cases explicit.</p>
  </div>
  <div class="rule-grid">
    <a class="rule-card" href="./LC012_OptimizeRemoveRange.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC012</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Optimize: Use ExecuteDelete() instead of RemoveRange()</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC012_OptimizeRemoveRange/</span>
    </a>
    <a class="rule-card" href="./LC032_ExecuteUpdateForBulkUpdates.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC032</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Use ExecuteUpdate for provable bulk scalar updates</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC032_ExecuteUpdateForBulkUpdates/</span>
    </a>
    <a class="rule-card" href="./LC035_MissingWhereBeforeExecuteDeleteUpdate.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC035</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Missing Where before bulk execute</h3>
      <span class="rule-card__meta">
        <span>Safety</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC035_MissingWhereBeforeExecuteDeleteUpdate/</span>
    </a>
  </div>
</section>

<section class="rule-domain" aria-labelledby="change-tracking-context-lifetime">
  <div class="rule-domain__heading">
    <h2 id="change-tracking-context-lifetime">Change Tracking &amp; Context Lifetime</h2>
    <p>Spot DbContext lifetime leaks, tracking-mode surprises, and writes that silently do nothing.</p>
  </div>
  <div class="rule-grid">
    <a class="rule-card" href="./LC009_MissingAsNoTracking.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC009</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Performance: Missing AsNoTracking() in Read-Only path</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC009_MissingAsNoTracking/</span>
    </a>
    <a class="rule-card" href="./LC010_SaveChangesInLoop.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC010</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>N+1 Write Problem: SaveChanges inside loop</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC010_SaveChangesInLoop/</span>
    </a>
    <a class="rule-card" href="./LC013_DisposedContextQuery.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC013</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Disposed Context Query</h3>
      <span class="rule-card__meta">
        <span>Reliability</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC013_DisposedContextQuery/</span>
    </a>
    <a class="rule-card" href="./LC025_AsNoTrackingWithUpdate.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC025</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Avoid AsNoTracking with Update/Remove</h3>
      <span class="rule-card__meta">
        <span>Reliability</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC025_AsNoTrackingWithUpdate/</span>
    </a>
    <a class="rule-card" href="./LC030_DbContextInSingleton.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC030</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Potential DbContext lifetime mismatch</h3>
      <span class="rule-card__meta">
        <span>Architecture</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC030_DbContextInSingleton/</span>
    </a>
    <a class="rule-card" href="./LC039_NestedSaveChanges.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC039</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Avoid repeated SaveChanges on the same context</h3>
      <span class="rule-card__meta">
        <span>Reliability</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC039_NestedSaveChanges/</span>
    </a>
    <a class="rule-card" href="./LC040_MixedTrackingAndNoTracking.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC040</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Avoid mixing tracking modes on the same context</h3>
      <span class="rule-card__meta">
        <span>Reliability</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC040_MixedTrackingAndNoTracking/</span>
    </a>
    <a class="rule-card" href="./LC044_AsNoTrackingThenModifySilentWrite.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC044</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>AsNoTracking query mutated then SaveChanges — silent data loss</h3>
      <span class="rule-card__meta">
        <span>Reliability</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC044_AsNoTrackingThenModify/</span>
    </a>
  </div>
</section>

<section class="rule-domain" aria-labelledby="execution-async">
  <div class="rule-domain__heading">
    <h2 id="execution-async">Execution &amp; Async</h2>
    <p>Find synchronous calls, repeated database execution, and async paths that drop cancellation or buffer too early.</p>
  </div>
  <div class="rule-grid">
    <a class="rule-card" href="./LC007_NPlusOneLooper.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC007</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>N+1 Problem: Database execution inside loop</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC007_NPlusOneLooper/</span>
    </a>
    <a class="rule-card" href="./LC008_SyncBlocker.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC008</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Sync-over-Async: Synchronous EF Core method in Async context</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC008_SyncBlocker/</span>
    </a>
    <a class="rule-card" href="./LC026_MissingCancellationToken.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC026</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Missing CancellationToken in async call</h3>
      <span class="rule-card__meta">
        <span>Reliability</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC026_MissingCancellationToken/</span>
    </a>
    <a class="rule-card" href="./LC036_DbContextCapturedAcrossThreads.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC036</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>DbContext captured by thread work item</h3>
      <span class="rule-card__meta">
        <span>Safety</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC036_DbContextCapturedAcrossThreads/</span>
    </a>
    <a class="rule-card" href="./LC043_AsyncEnumerableBuffering.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC043</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Prefer await foreach over buffering async streams</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC043_AsyncEnumerableBuffering/</span>
    </a>
    <a class="rule-card" href="./LC046_ConcurrentDbContextOperations.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC046</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Concurrent EF Core operations on the same DbContext</h3>
      <span class="rule-card__meta">
        <span>Safety</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC046_ConcurrentDbContextOperations/</span>
    </a>
  </div>
</section>

<section class="rule-domain" aria-labelledby="loading-includes">
  <div class="rule-domain__heading">
    <h2 id="loading-includes">Loading &amp; Includes</h2>
    <p>Make relationship loading deliberate before N+1 round trips or over-eager include graphs reach production.</p>
  </div>
  <div class="rule-grid">
    <a class="rule-card" href="./LC006_CartesianExplosion.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC006</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Cartesian Explosion Risk: Multiple Collection Includes</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC006_CartesianExplosion/</span>
    </a>
    <a class="rule-card" href="./LC019_ConditionalInclude.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC019</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Conditional Include Expression</h3>
      <span class="rule-card__meta">
        <span>Correctness</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC019_ConditionalInclude/</span>
    </a>
    <a class="rule-card" href="./LC028_DeepThenInclude.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC028</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Deep ThenInclude Chain</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC028_DeepThenInclude/</span>
    </a>
    <a class="rule-card" href="./LC038_ExcessiveEagerLoading.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC038</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Avoid excessive eager loading</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC038_ExcessiveEagerLoading/</span>
    </a>
    <a class="rule-card" href="./LC042_MissingQueryTags.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC042</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Complex query should be tagged</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC042_MissingQueryTags/</span>
    </a>
    <a class="rule-card" href="./LC045_MissingInclude.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC045</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Missing Include: navigation accessed on materialized entity</h3>
      <span class="rule-card__meta">
        <span>Reliability</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC045_MissingInclude/</span>
    </a>
  </div>
</section>

<section class="rule-domain" aria-labelledby="materialization-projection">
  <div class="rule-domain__heading">
    <h2 id="materialization-projection">Materialization &amp; Projection</h2>
    <p>Keep work in SQL where it belongs and avoid loading whole entities or unbounded result sets by accident.</p>
  </div>
  <div class="rule-grid">
    <a class="rule-card" href="./LC002_PrematureMaterialization.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC002</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Premature query continuation after materialization</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC002_PrematureMaterialization/</span>
    </a>
    <a class="rule-card" href="./LC003_AnyOverCount.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC003</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Prefer Any() over Count() existence checks</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC003_AnyOverCount/</span>
    </a>
    <a class="rule-card" href="./LC017_WholeEntityProjection.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC017</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Performance: Consider using Select() projection</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC017_WholeEntityProjection/</span>
    </a>
    <a class="rule-card" href="./LC022_ToListInSelectProjection.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC022</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Nested collection materialization inside projection</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC022_ToListInSelectProjection/</span>
    </a>
    <a class="rule-card" href="./LC023_FindInsteadOfFirstOrDefault.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC023</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Use Find/FindAsync for primary key lookups</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC023_FindInsteadOfFirstOrDefault/</span>
    </a>
    <a class="rule-card" href="./LC029_RedundantIdentitySelect.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC029</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Redundant identity Select</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC029_RedundantIdentitySelect/</span>
    </a>
    <a class="rule-card" href="./LC031_UnboundedQueryMaterialization.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC031</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Unbounded Query Materialization</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC031_UnboundedQueryMaterialization/</span>
    </a>
    <a class="rule-card" href="./LC033_UseFrozenSetForStaticMembershipCaches.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC033</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Use FrozenSet for provably read-only membership caches</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC033_UseFrozenSetForStaticMembershipCaches/</span>
    </a>
    <a class="rule-card" href="./LC041_SingleEntityScalarProjection.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC041</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Single entity query over-fetches one consumed property</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC041_SingleEntityScalarProjection/</span>
    </a>
  </div>
</section>

<section class="rule-domain" aria-labelledby="query-shape-translation">
  <div class="rule-domain__heading">
    <h2 id="query-shape-translation">Query Shape &amp; Translation</h2>
    <p>Catch LINQ patterns that EF Core cannot translate reliably or cannot page deterministically.</p>
  </div>
  <div class="rule-grid">
    <a class="rule-card" href="./LC001_LocalMethod.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC001</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Client-side evaluation risk: Local method usage in IQueryable</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC001_LocalMethod/</span>
    </a>
    <a class="rule-card" href="./LC004_IQueryableLeak.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC004</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Deferred Execution Leak: IQueryable passed as IEnumerable</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC004_IQueryableLeak/</span>
    </a>
    <a class="rule-card" href="./LC005_MultipleOrderBy.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC005</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Multiple OrderBy calls</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC005_MultipleOrderBy/</span>
    </a>
    <a class="rule-card" href="./LC014_AvoidStringCaseConversion.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC014</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Avoid String.ToLower() or ToUpper() in LINQ queries</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC014_AvoidStringCaseConversion/</span>
    </a>
    <a class="rule-card" href="./LC015_MissingOrderBy.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC015</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Deterministic Pagination: OrderBy required before Skip/Take</h3>
      <span class="rule-card__meta">
        <span>Reliability</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC015_MissingOrderBy/</span>
    </a>
    <a class="rule-card" href="./LC016_AvoidDateTimeNow.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC016</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Avoid DateTime.Now/UtcNow in LINQ queries</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC016_AvoidDateTimeNow/</span>
    </a>
    <a class="rule-card" href="./LC020_StringContainsWithComparison.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC020</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Avoid untranslatable string comparison overloads</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC020_StringContainsWithComparison/</span>
    </a>
    <a class="rule-card" href="./LC024_GroupByNonTranslatable.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC024</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>GroupBy with Non-Translatable Projection</h3>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC024_GroupByNonTranslatable/</span>
    </a>
  </div>
</section>

<section class="rule-domain" aria-labelledby="raw-sql-security">
  <div class="rule-domain__heading">
    <h2 id="raw-sql-security">Raw SQL &amp; Security</h2>
    <p>Flag SQL construction patterns that can bypass parameterization, tenant filters, or review expectations.</p>
  </div>
  <div class="rule-grid">
    <a class="rule-card" href="./LC018_AvoidFromSqlRawWithInterpolation.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC018</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Avoid FromSqlRaw with interpolated strings</h3>
      <span class="rule-card__meta">
        <span>Security</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC018_AvoidFromSqlRawWithInterpolation/</span>
    </a>
    <a class="rule-card" href="./LC021_AvoidIgnoreQueryFilters.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC021</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Avoid IgnoreQueryFilters</h3>
      <span class="rule-card__meta">
        <span>Security</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC021_AvoidIgnoreQueryFilters/</span>
    </a>
    <a class="rule-card" href="./LC034_AvoidExecuteSqlRawWithInterpolation.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC034</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Avoid ExecuteSqlRaw with interpolated strings</h3>
      <span class="rule-card__meta">
        <span>Security</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC034_AvoidExecuteSqlRawWithInterpolation/</span>
    </a>
    <a class="rule-card" href="./LC037_RawSqlStringConstruction.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC037</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Avoid constructed raw SQL strings</h3>
      <span class="rule-card__meta">
        <span>Security</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC037_RawSqlStringConstruction/</span>
    </a>
  </div>
</section>

<section class="rule-domain" aria-labelledby="schema-modeling">
  <div class="rule-domain__heading">
    <h2 id="schema-modeling">Schema &amp; Modeling</h2>
    <p>Guard model shape choices that produce fragile entity mappings and unclear relationships.</p>
  </div>
  <div class="rule-grid">
    <a class="rule-card" href="./LC011_EntityMissingPrimaryKey.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC011</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Design: Entity missing Primary Key</h3>
      <span class="rule-card__meta">
        <span>Design</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC011_EntityMissingPrimaryKey/</span>
    </a>
    <a class="rule-card" href="./LC027_MissingExplicitForeignKey.html">
      <span class="rule-card__top">
        <span class="rule-card__id">LC027</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Missing Explicit Foreign Key Property</h3>
      <span class="rule-card__meta">
        <span>Design</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC027_MissingExplicitForeignKey/</span>
    </a>
  </div>
</section>

