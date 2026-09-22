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
    <div class="metric"><strong>52</strong><span>rules</span></div>
    <div class="metric"><strong>33</strong><span>warnings</span></div>
    <div class="metric"><strong>35</strong><span>code fixes</span></div>
  </div>
</section>

<p class="eyebrow">8 diagnostic domains</p>

<form class="catalog-filter" role="search" aria-label="Filter rules" hidden>
  <label class="catalog-filter__label" for="rule-filter">Find a rule</label>
  <input id="rule-filter" type="search" placeholder="Search by ID or keyword: LC007, Include, raw SQL" autocomplete="off" spellcheck="false">
  <div class="catalog-filter__toggles">
    <label><input type="checkbox" data-filter="fix"> Has a code fix</label>
    <label><input type="checkbox" data-filter="warning"> Warnings only</label>
  </div>
  <p class="catalog-filter__status" aria-live="polite"></p>
</form>
<script src="./assets/js/rule-catalog.js" defer></script>

<section class="rule-domain" aria-labelledby="bulk-operations-set-based-writes">
  <div class="rule-domain__heading">
    <h2 id="bulk-operations-set-based-writes">Bulk Operations &amp; Set-Based Writes</h2>
    <p>Keep destructive and high-volume writes set-based while making the risky cases explicit.</p>
  </div>
  <div class="rule-grid">
    <a class="rule-card" href="./LC012_OptimizeRemoveRange.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC012</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Optimize: Use ExecuteDelete() instead of RemoveRange()</h3>
      <p class="rule-card__summary">Suggests ExecuteDelete() instead of RemoveRange() for EF Core bulk deletes, removing rows in one SQL DELETE without loading them first.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC012_OptimizeRemoveRange/</span>
    </a>
    <a class="rule-card" href="./LC032_ExecuteUpdateForBulkUpdates.html" data-severity="info" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC032</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Use ExecuteUpdate for provable bulk scalar updates</h3>
      <p class="rule-card__summary">Detects loops that load EF Core entities only to set scalar properties and save, and suggests one set-based ExecuteUpdate() call instead.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC032_ExecuteUpdateForBulkUpdates/</span>
    </a>
    <a class="rule-card" href="./LC035_MissingWhereBeforeExecuteDeleteUpdate.html" data-severity="info" data-fix="false">
      <span class="rule-card__top">
        <span class="rule-card__id">LC035</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Missing Where before bulk execute</h3>
      <p class="rule-card__summary">Flags EF Core ExecuteDelete and ExecuteUpdate calls with no proven Where filter, which delete or rewrite every row in the table.</p>
      <span class="rule-card__meta">
        <span>Safety</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC035_MissingWhereBeforeExecuteDeleteUpdate/</span>
    </a>
    <a class="rule-card" href="./LC047_ExecuteDeleteBypassesTrackedDelete.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC047</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>ExecuteDelete bypasses the tracked delete pipeline</h3>
      <p class="rule-card__summary">Flags ExecuteDelete when SaveChanges logic such as soft delete or client cascades must run, since SQL DELETE skips that tracked pipeline.</p>
      <span class="rule-card__meta">
        <span>Safety</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC047_ExecuteDeleteBypassesTrackedDelete/</span>
    </a>
  </div>
</section>

<section class="rule-domain" aria-labelledby="change-tracking-context-lifetime">
  <div class="rule-domain__heading">
    <h2 id="change-tracking-context-lifetime">Change Tracking &amp; Context Lifetime</h2>
    <p>Spot DbContext lifetime leaks, tracking-mode surprises, and writes that silently do nothing.</p>
  </div>
  <div class="rule-grid">
    <a class="rule-card" href="./LC009_MissingAsNoTracking.html" data-severity="info" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC009</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Performance: Missing AsNoTracking() in Read-Only path</h3>
      <p class="rule-card__summary">Suggests AsNoTracking() for read-only EF Core queries, so the change tracker does not snapshot entities the code never modifies.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC009_MissingAsNoTracking/</span>
    </a>
    <a class="rule-card" href="./LC010_SaveChangesInLoop.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC010</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>N+1 Write Problem: SaveChanges inside loop</h3>
      <p class="rule-card__summary">Flags SaveChanges or SaveChangesAsync inside a loop, which costs one database round trip per item instead of one batched save.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC010_SaveChangesInLoop/</span>
    </a>
    <a class="rule-card" href="./LC013_DisposedContextQuery.html" data-severity="warning" data-fix="false">
      <span class="rule-card__top">
        <span class="rule-card__id">LC013</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Disposed Context Query</h3>
      <p class="rule-card__summary">Detects an IQueryable or IAsyncEnumerable returned after its DbContext is disposed, which throws when the caller finally enumerates it.</p>
      <span class="rule-card__meta">
        <span>Reliability</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC013_DisposedContextQuery/</span>
    </a>
    <a class="rule-card" href="./LC025_AsNoTrackingWithUpdate.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC025</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Avoid AsNoTracking with Update/Remove</h3>
      <p class="rule-card__summary">Detects entities loaded with AsNoTracking() and then passed to Update or Remove, which can overwrite every column or fail on identity conflicts.</p>
      <span class="rule-card__meta">
        <span>Reliability</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC025_AsNoTrackingWithUpdate/</span>
    </a>
    <a class="rule-card" href="./LC030_DbContextInSingleton.html" data-severity="info" data-fix="false">
      <span class="rule-card__top">
        <span class="rule-card__id">LC030</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Potential DbContext lifetime mismatch</h3>
      <p class="rule-card__summary">Flags singletons and hosted services that store a DbContext, a lifetime mismatch that leads to threading errors and stale data. Use a factory.</p>
      <span class="rule-card__meta">
        <span>Architecture</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC030_DbContextInSingleton/</span>
    </a>
    <a class="rule-card" href="./LC039_NestedSaveChanges.html" data-severity="info" data-fix="false">
      <span class="rule-card__top">
        <span class="rule-card__id">LC039</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Avoid repeated SaveChanges on the same context</h3>
      <p class="rule-card__summary">Flags repeated SaveChanges calls on the same DbContext in one method, which add round trips and can leave partial writes. Save once.</p>
      <span class="rule-card__meta">
        <span>Reliability</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC039_NestedSaveChanges/</span>
    </a>
    <a class="rule-card" href="./LC040_MixedTrackingAndNoTracking.html" data-severity="info" data-fix="false">
      <span class="rule-card__top">
        <span class="rule-card__id">LC040</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Avoid mixing tracking modes on the same context</h3>
      <p class="rule-card__summary">Flags methods that mix tracked and AsNoTracking() queries on the same DbContext, which makes later update behavior hard to predict.</p>
      <span class="rule-card__meta">
        <span>Reliability</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC040_MixedTrackingAndNoTracking/</span>
    </a>
    <a class="rule-card" href="./LC044_AsNoTrackingThenModifySilentWrite.html" data-severity="warning" data-fix="false">
      <span class="rule-card__top">
        <span class="rule-card__id">LC044</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>AsNoTracking query mutated then SaveChanges — silent data loss</h3>
      <p class="rule-card__summary">Detects entities loaded with AsNoTracking(), modified, then followed by SaveChanges, which silently saves nothing: data loss with no error.</p>
      <span class="rule-card__meta">
        <span>Reliability</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC044_AsNoTrackingThenModify/</span>
    </a>
    <a class="rule-card" href="./LC048_LostUpdateRisk.html" data-severity="warning" data-fix="false">
      <span class="rule-card__top">
        <span class="rule-card__id">LC048</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Tracked update can overwrite a concurrent change</h3>
      <p class="rule-card__summary">Detects tracked EF Core read-modify-write updates, such as counters, saved without a concurrency token, so concurrent requests overwrite each other.</p>
      <span class="rule-card__meta">
        <span>Reliability</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC048_LostUpdateRisk/</span>
    </a>
  </div>
</section>

<section class="rule-domain" aria-labelledby="execution-async">
  <div class="rule-domain__heading">
    <h2 id="execution-async">Execution &amp; Async</h2>
    <p>Find synchronous calls, repeated database execution, and async paths that drop cancellation or buffer too early.</p>
  </div>
  <div class="rule-grid">
    <a class="rule-card" href="./LC007_NPlusOneLooper.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC007</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>N+1 Problem: Database execution inside loop</h3>
      <p class="rule-card__summary">Finds EF Core N+1 queries: Find, ToList, Count and other database calls that provably run once per loop iteration instead of once.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC007_NPlusOneLooper/</span>
    </a>
    <a class="rule-card" href="./LC008_SyncBlocker.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC008</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Sync-over-Async: Synchronous EF Core method in Async context</h3>
      <p class="rule-card__summary">Flags synchronous EF Core calls such as ToList or SaveChanges inside async methods, where they block threads. Use the Async counterpart.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC008_SyncBlocker/</span>
    </a>
    <a class="rule-card" href="./LC026_MissingCancellationToken.html" data-severity="info" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC026</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Missing CancellationToken in async call</h3>
      <p class="rule-card__summary">Flags EF Core async calls that omit an available CancellationToken, so cancelled requests keep running queries against the database.</p>
      <span class="rule-card__meta">
        <span>Reliability</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC026_MissingCancellationToken/</span>
    </a>
    <a class="rule-card" href="./LC036_DbContextCapturedAcrossThreads.html" data-severity="warning" data-fix="false">
      <span class="rule-card__top">
        <span class="rule-card__id">LC036</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>DbContext captured by thread work item</h3>
      <p class="rule-card__summary">Detects one DbContext captured by Task.Run, Parallel.ForEach, threads or timers. DbContext is not thread-safe, so use one context per task.</p>
      <span class="rule-card__meta">
        <span>Safety</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC036_DbContextCapturedAcrossThreads/</span>
    </a>
    <a class="rule-card" href="./LC043_AsyncEnumerableBuffering.html" data-severity="info" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC043</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Prefer await foreach over buffering async streams</h3>
      <p class="rule-card__summary">Flags an IAsyncEnumerable buffered with ToListAsync or ToArrayAsync only to loop over it once. Use await foreach to stream instead.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC043_AsyncEnumerableBuffering/</span>
    </a>
    <a class="rule-card" href="./LC046_ConcurrentDbContextOperations.html" data-severity="warning" data-fix="false">
      <span class="rule-card__top">
        <span class="rule-card__id">LC046</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Concurrent EF Core operations on the same DbContext</h3>
      <p class="rule-card__summary">Detects overlapping async operations on the same DbContext instance, which EF Core rejects at runtime. Await each call or use separate contexts.</p>
      <span class="rule-card__meta">
        <span>Safety</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC046_ConcurrentDbContextOperations/</span>
    </a>
    <a class="rule-card" href="./LC051_ToAsyncEnumerableOnQuery.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC051</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>ToAsyncEnumerable() runs an EF Core query synchronously</h3>
      <p class="rule-card__summary">Flags ToAsyncEnumerable() on EF Core queries, which enumerates them synchronously and blocks a thread per row, and switches to AsAsyncEnumerable().</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC051_ToAsyncEnumerableOnQuery/</span>
    </a>
  </div>
</section>

<section class="rule-domain" aria-labelledby="loading-includes">
  <div class="rule-domain__heading">
    <h2 id="loading-includes">Loading &amp; Includes</h2>
    <p>Make relationship loading deliberate before N+1 round trips or over-eager include graphs reach production.</p>
  </div>
  <div class="rule-grid">
    <a class="rule-card" href="./LC006_CartesianExplosion.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC006</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Cartesian Explosion Risk: Multiple Collection Includes</h3>
      <p class="rule-card__summary">Detects sibling collection Include paths without AsSplitQuery(), where EF Core joins them into a Cartesian product that multiplies the rows returned.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC006_CartesianExplosion/</span>
    </a>
    <a class="rule-card" href="./LC019_ConditionalInclude.html" data-severity="warning" data-fix="false">
      <span class="rule-card__top">
        <span class="rule-card__id">LC019</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Conditional Include Expression</h3>
      <p class="rule-card__summary">Flags ternary and null-coalescing expressions inside EF Core Include paths, which cannot translate. Apply the Include conditionally instead.</p>
      <span class="rule-card__meta">
        <span>Correctness</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC019_ConditionalInclude/</span>
    </a>
    <a class="rule-card" href="./LC028_DeepThenInclude.html" data-severity="warning" data-fix="false">
      <span class="rule-card__top">
        <span class="rule-card__id">LC028</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Deep ThenInclude Chain</h3>
      <p class="rule-card__summary">Flags EF Core ThenInclude chains deeper than the configured limit, which load large object graphs. Project or split the query instead.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC028_DeepThenInclude/</span>
    </a>
    <a class="rule-card" href="./LC038_ExcessiveEagerLoading.html" data-severity="info" data-fix="false">
      <span class="rule-card__top">
        <span class="rule-card__id">LC038</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Avoid excessive eager loading</h3>
      <p class="rule-card__summary">Flags EF Core queries with more Include and ThenInclude steps than the configured threshold, which load oversized graphs in one query.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC038_ExcessiveEagerLoading/</span>
    </a>
    <a class="rule-card" href="./LC042_MissingQueryTags.html" data-severity="info" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC042</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Complex query should be tagged</h3>
      <p class="rule-card__summary">Flags complex EF Core queries without TagWith() or TagWithCallSite(), so the SQL they produce is hard to trace back to code in logs.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC042_MissingQueryTags/</span>
    </a>
    <a class="rule-card" href="./LC045_MissingInclude.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC045</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Missing Include: navigation accessed on materialized entity</h3>
      <p class="rule-card__summary">Detects navigation properties read after an EF Core query without a matching Include, causing N+1 lazy loads or null and empty data.</p>
      <span class="rule-card__meta">
        <span>Reliability</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC045_MissingInclude/</span>
    </a>
    <a class="rule-card" href="./LC049_IncludeIgnoredByProjection.html" data-severity="info" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC049</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Include is ignored by a Select projection</h3>
      <p class="rule-card__summary">Flags EF Core Include calls that a later Select projection makes EF Core ignore, and removes them.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC049_IncludeIgnoredByProjection/</span>
    </a>
  </div>
</section>

<section class="rule-domain" aria-labelledby="materialization-projection">
  <div class="rule-domain__heading">
    <h2 id="materialization-projection">Materialization &amp; Projection</h2>
    <p>Keep work in SQL where it belongs and avoid loading whole entities or unbounded result sets by accident.</p>
  </div>
  <div class="rule-grid">
    <a class="rule-card" href="./LC002_PrematureMaterialization.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC002</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Premature query continuation after materialization</h3>
      <p class="rule-card__summary">Catches ToList, ToArray or AsEnumerable before Where, Select or OrderBy, which moves EF Core query work out of SQL and into memory.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC002_PrematureMaterialization/</span>
    </a>
    <a class="rule-card" href="./LC003_AnyOverCount.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC003</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Prefer Any() over Count() existence checks</h3>
      <p class="rule-card__summary">Flags Count() &gt; 0 style existence checks on EF Core queries and suggests Any() or AnyAsync(), which can stop at the first matching row.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC003_AnyOverCount/</span>
    </a>
    <a class="rule-card" href="./LC017_WholeEntityProjection.html" data-severity="info" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC017</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Performance: Consider using Select() projection</h3>
      <p class="rule-card__summary">Flags EF Core queries that load whole entities when only a few properties are used. Project with Select to fetch just the needed columns.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC017_WholeEntityProjection/</span>
    </a>
    <a class="rule-card" href="./LC022_ToListInSelectProjection.html" data-severity="info" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC022</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Nested collection materialization inside projection</h3>
      <p class="rule-card__summary">Flags ToList or ToArray on nested collections inside EF Core Select projections, which can be expensive or translate differently per provider.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC022_ToListInSelectProjection/</span>
    </a>
    <a class="rule-card" href="./LC023_FindInsteadOfFirstOrDefault.html" data-severity="info" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC023</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Use Find/FindAsync for primary key lookups</h3>
      <p class="rule-card__summary">Suggests Find or FindAsync when FirstOrDefault or SingleOrDefault looks up an EF Core entity by primary key, so tracked entities skip the database.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC023_FindInsteadOfFirstOrDefault/</span>
    </a>
    <a class="rule-card" href="./LC029_RedundantIdentitySelect.html" data-severity="info" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC029</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Redundant identity Select</h3>
      <p class="rule-card__summary">Flags Select(x =&gt; x) in LINQ and EF Core query chains. The identity projection does nothing and only adds noise to the expression tree.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC029_RedundantIdentitySelect/</span>
    </a>
    <a class="rule-card" href="./LC031_UnboundedQueryMaterialization.html" data-severity="info" data-fix="false">
      <span class="rule-card__top">
        <span class="rule-card__id">LC031</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Unbounded Query Materialization</h3>
      <p class="rule-card__summary">Flags EF Core queries materialized with no filter or Take limit, which can load an entire table into memory as the data grows.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC031_UnboundedQueryMaterialization/</span>
    </a>
    <a class="rule-card" href="./LC033_UseFrozenSetForStaticMembershipCaches.html" data-severity="info" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC033</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Use FrozenSet for provably read-only membership caches</h3>
      <p class="rule-card__summary">Flags private static readonly HashSet lookup caches that are never mutated and suggests FrozenSet on .NET 8+ for faster membership checks.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC033_UseFrozenSetForStaticMembershipCaches/</span>
    </a>
    <a class="rule-card" href="./LC041_SingleEntityScalarProjection.html" data-severity="info" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC041</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Single entity query over-fetches one consumed property</h3>
      <p class="rule-card__summary">Flags First or Single queries that load a whole EF Core entity when only one property is read. Select that property to fetch one column.</p>
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
    <a class="rule-card" href="./LC001_LocalMethod.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC001</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Client-side evaluation risk: Local method usage in IQueryable</h3>
      <p class="rule-card__summary">Flags your own helper methods inside EF Core IQueryable lambdas that SQL cannot translate, which forces client-side evaluation or a runtime error.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC001_LocalMethod/</span>
    </a>
    <a class="rule-card" href="./LC004_IQueryableLeak.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC004</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Deferred Execution Leak: IQueryable passed as IEnumerable</h3>
      <p class="rule-card__summary">Detects an EF Core IQueryable passed as IEnumerable to a method that enumerates it, so filtering runs in memory instead of in SQL.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC004_IQueryableLeak/</span>
    </a>
    <a class="rule-card" href="./LC005_MultipleOrderBy.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC005</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Multiple OrderBy calls</h3>
      <p class="rule-card__summary">Flags a second OrderBy or OrderByDescending in one LINQ chain, which silently discards the first sort. Use ThenBy or ThenByDescending.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC005_MultipleOrderBy/</span>
    </a>
    <a class="rule-card" href="./LC014_AvoidStringCaseConversion.html" data-severity="warning" data-fix="false">
      <span class="rule-card__top">
        <span class="rule-card__id">LC014</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Avoid String.ToLower() or ToUpper() in LINQ queries</h3>
      <p class="rule-card__summary">Flags ToLower() and ToUpper() on entity properties in EF Core queries, which can stop the database using an index. Use a collation instead.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC014_AvoidStringCaseConversion/</span>
    </a>
    <a class="rule-card" href="./LC015_MissingOrderBy.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC015</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Deterministic Pagination: OrderBy required before Skip/Take</h3>
      <p class="rule-card__summary">Flags Skip, Take, Last, ElementAt and Chunk on unordered EF Core queries, where the database may return rows in a different order each run.</p>
      <span class="rule-card__meta">
        <span>Reliability</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC015_MissingOrderBy/</span>
    </a>
    <a class="rule-card" href="./LC016_AvoidDateTimeNow.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC016</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Avoid DateTime.Now/UtcNow in LINQ queries</h3>
      <p class="rule-card__summary">Flags DateTime.Now, UtcNow and DateTimeOffset.Now inside EF Core LINQ queries. Hoist the value into a local for cacheable, testable queries.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC016_AvoidDateTimeNow/</span>
    </a>
    <a class="rule-card" href="./LC020_StringContainsWithComparison.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC020</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Avoid untranslatable string comparison overloads</h3>
      <p class="rule-card__summary">Flags Contains, StartsWith and EndsWith with a StringComparison argument in EF Core queries, which providers often cannot translate to SQL.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC020_StringContainsWithComparison/</span>
    </a>
    <a class="rule-card" href="./LC024_GroupByNonTranslatable.html" data-severity="warning" data-fix="false">
      <span class="rule-card__top">
        <span class="rule-card__id">LC024</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>GroupBy with Non-Translatable Projection</h3>
      <p class="rule-card__summary">Flags EF Core GroupBy queries that project values SQL cannot translate, which causes runtime errors or accidental client-side grouping.</p>
      <span class="rule-card__meta">
        <span>Performance</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC024_GroupByNonTranslatable/</span>
    </a>
    <a class="rule-card" href="./LC050_OrderByBeforeDistinct.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC050</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>OrderBy before Distinct is discarded</h3>
      <p class="rule-card__summary">Flags EF Core queries that sort before Distinct(), where SQL DISTINCT silently drops the ORDER BY, and moves the sort after Distinct().</p>
      <span class="rule-card__meta">
        <span>Correctness</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC050_OrderByBeforeDistinct/</span>
    </a>
  </div>
</section>

<section class="rule-domain" aria-labelledby="raw-sql-security">
  <div class="rule-domain__heading">
    <h2 id="raw-sql-security">Raw SQL &amp; Security</h2>
    <p>Flag SQL construction patterns that can bypass parameterization, tenant filters, or review expectations.</p>
  </div>
  <div class="rule-grid">
    <a class="rule-card" href="./LC018_AvoidFromSqlRawWithInterpolation.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC018</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Avoid FromSqlRaw with interpolated strings</h3>
      <p class="rule-card__summary">Flags FromSqlRaw and SqlQueryRaw called with interpolated or concatenated SQL, an injection risk. Use FromSql or SQL parameters instead.</p>
      <span class="rule-card__meta">
        <span>Security</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC018_AvoidFromSqlRawWithInterpolation/</span>
    </a>
    <a class="rule-card" href="./LC021_AvoidIgnoreQueryFilters.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC021</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Avoid IgnoreQueryFilters</h3>
      <p class="rule-card__summary">Flags IgnoreQueryFilters() in EF Core queries, which bypasses global filters for soft delete, multi-tenancy or security and can leak data.</p>
      <span class="rule-card__meta">
        <span>Security</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC021_AvoidIgnoreQueryFilters/</span>
    </a>
    <a class="rule-card" href="./LC034_AvoidExecuteSqlRawWithInterpolation.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC034</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Avoid ExecuteSqlRaw with interpolated strings</h3>
      <p class="rule-card__summary">Flags ExecuteSqlRaw and ExecuteSqlRawAsync called with interpolated or concatenated SQL, an injection risk. Use ExecuteSql or parameters.</p>
      <span class="rule-card__meta">
        <span>Security</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC034_AvoidExecuteSqlRawWithInterpolation/</span>
    </a>
    <a class="rule-card" href="./LC037_RawSqlStringConstruction.html" data-severity="warning" data-fix="false">
      <span class="rule-card__top">
        <span class="rule-card__id">LC037</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Avoid constructed raw SQL strings</h3>
      <p class="rule-card__summary">Flags SQL built with concatenation, string.Format or StringBuilder before it reaches FromSqlRaw, ExecuteSqlRaw or SqlQueryRaw: an injection risk.</p>
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
    <a class="rule-card" href="./LC011_EntityMissingPrimaryKey.html" data-severity="warning" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC011</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Design: Entity missing Primary Key</h3>
      <p class="rule-card__summary">Detects EF Core entity types exposed through a DbSet that have no primary key from convention, a [Key] attribute or fluent configuration.</p>
      <span class="rule-card__meta">
        <span>Design</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC011_EntityMissingPrimaryKey/</span>
    </a>
    <a class="rule-card" href="./LC027_MissingExplicitForeignKey.html" data-severity="info" data-fix="true">
      <span class="rule-card__top">
        <span class="rule-card__id">LC027</span>
        <span class="pill pill--info">Info</span>
      </span>
      <h3>Missing Explicit Foreign Key Property</h3>
      <p class="rule-card__summary">Flags EF Core reference navigations whose dependent entity has no matching foreign key property, leaving EF Core to create a shadow key.</p>
      <span class="rule-card__meta">
        <span>Design</span>
        <span class="pill pill--fix">Code fix</span>
      </span>
      <span class="rule-card__sample">Samples/LC027_MissingExplicitForeignKey/</span>
    </a>
    <a class="rule-card" href="./LC052_NonDeterministicModelData.html" data-severity="warning" data-fix="false">
      <span class="rule-card__top">
        <span class="rule-card__id">LC052</span>
        <span class="pill pill--warning">Warning</span>
      </span>
      <h3>Model data uses a value that changes on every run</h3>
      <p class="rule-card__summary">Flags HasData and HasDefaultValue with DateTime.Now or Guid.NewGuid(), which change the EF Core model every run and make EF Core 9 Migrate() throw.</p>
      <span class="rule-card__meta">
        <span>Reliability</span>
        <span class="pill pill--manual">Manual only</span>
      </span>
      <span class="rule-card__sample">Samples/LC052_NonDeterministicModelData/</span>
    </a>
  </div>
</section>

