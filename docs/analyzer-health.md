# Analyzer Health

Reviewed: 2026-06-10 (full per-rule re-verification; previous overnight rerun 2026-06-04, deep rescan 2026-05-29)

This is a deliberately harsh health audit for the **45 analyzers** in `RuleCatalog`. The catalog currently declares 30 rules with code fixes and 15 manual-only rules with explicit rationale. Scores are 1-5, where `5` means reference-quality and hard to improve, `3` means usable but meaningfully incomplete, and `1` means unreliable or underbuilt.

## Rubric

| Metric | Meaning |
| --- | --- |
| Analyzer | Semantic depth, EF-awareness, flow/alias handling, and diagnostic placement accuracy. |
| False Positives | Conservatism around ambiguous sources, intentional usage, provider/version differences, opt-outs, and non-EF boundaries. |
| Fix Strategy | Safety and completeness of the fixer, or the strength of the manual-only rationale when no safe fixer exists. |
| Tests | Strength of analyzer, fixer, negative, edge-case, config, and cross-analyzer tests. |
| Docs/Samples | Clarity and consistency of rule docs, sample coverage, metadata, documented safe cases, and documented non-goals. |
| Importance | User-facing usefulness based on frequency, severity, security/reliability/performance impact, and actionability. |

Harsh calibration notes:

- A `5` is rare. For a fixable rule it requires broad analyzer coverage plus dedicated fixer coverage and strong negative tests. If a single named gap exists, the score is not a `5`.
- A `4` means strong and healthy, only minor refinements remain.
- A `3` is the default for rules that work but have visible gaps in tests, docs, or fixer rationale.
- A manual-only rule can score well on Fix Strategy, but it still needs explicit intentional-use guidance and negative tests.
- Info rules are scored by product value, not implementation effort. Some are healthy but still low importance.
- Docs/Samples score existence and quality. Every rule has a doc and sample, but a 30-line doc covering only the violation/fix without provider variance, intentional-use patterns, or non-goals will not score above `3`.
- Importance is re-evaluated against modern EF Core 9+. Rules that mattered most against older providers or pre-`ExecuteUpdate`/`ExecuteDelete` EF are intentionally pulled down when modern semantics or split-query support reduces real-world impact.
- A newly shipped rule that crashed the compiler in its first release does **not** score like a mature rule on Analyzer/FP, regardless of fix velocity (see LC045).

Priority is a planning signal: `High` means the analyzer is important and has meaningful health gaps, `Medium` means useful follow-up work is warranted, and `Low` means no immediate work is needed.

## Scorecard

> Rows below are the **2026-06-10 re-verified state** — the 2026-06-04 rerun deltas (5.5.1–5.5.13) and the 5.6.0/5.6.1 LC045 releases are folded into the rows. Every row was independently re-checked against current source, tests, and docs by a six-probe parallel audit; the full suite passes at **1006 tests** on net10.0.

| Rule | Title | Domain | Severity | Analyzer | False Positives | Fix Strategy | Tests | Docs/Samples | Importance | Priority | Notes |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| LC001 | Local method usage in IQueryable | Query Shape & Translation | Warning | 4 | 3 | 2 | 3 | 3 | 3 | Low | Detection is sound and now includes aggregate selectors (`Sum`/`Average`/`Min`/`Max` selector FN fixed in 5.5.11). Fixer remains a thin `AsEnumerable()` guard (2 fixer tests, no guidance on parameterized constants or translatable SQL functions); doc lacks the client-eval trade-off. Modern EF partial client evaluation keeps urgency moderate. |
| LC002 | Premature query continuation after materialization | Materialization & Projection | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | Strong analyzer/fixer pair (45 tests, 14 fixer). De-duplicating set sources (`ToHashSet().ToList()`) and keyed/grouped sources (`ToDictionary().ToList()`, 5.5.4) are no longer treated as redundant, closing both the unsafe fix and the misleading message. Doc still sparse on `ToList()`/`ToArray()`/`AsEnumerable()` variance and intentional client-boundary patterns. |
| LC003 | Prefer Any() over Count() existence checks | Materialization & Projection | Warning | 3 | 4 | 4 | 3 | 2 | 3 | Low | Single-shape binary-comparison detection with a safe fixer; the 2026-06-04 Round-2 probe found no defects ("robust" on `== 0` / `> 0` / reversed / const-folded / cast forms). Doc is boilerplate (31 lines, no perf/provider nuance, no scalar-context guidance). |
| LC004 | IQueryable passed as IEnumerable | Query Shape & Translation | Warning | 4 | 4 | 3 | 4 | 3 | 4 | Low | Solid API-boundary analyzer proving foreach/forwarding/materializing sinks; 5.5.10 closed the query-expression FN (a C# query expression is now followed to its source parameter). Docs short on forwarding chains and method-body scoping nuance. Nested-local-function skipping stays a deliberate FP-avoidance non-goal. |
| LC005 | Multiple OrderBy calls | Query Shape & Translation | Warning | 4 | 4 | 4 | 3 | 3 | 3 | Low | Linear chain heuristic with a safe `ThenBy` fixer; the query-comprehension crash (`orderby a orderby b` → AD0001) is fixed and reported at the `orderby` clause (report-only). Tests thin (12 methods, 3 fixer). Residual: chains broken across intervening operators or locals are not walked. |
| LC006 | Multiple collection Includes | Loading & Includes | Warning | 4 | 4 | 3 | 4 | 4 | 4 | Low | High-impact rule with a sibling-collection precision contract; `LocalAssignmentCache` follows single-assignment locals (closing both the `AsSplitQuery()`-across-a-local FP and the split-sibling FN). Include-path parsing now lives in the shared `IncludePathParser` (5.6.0 refactor, shared with LC045). 94-line doc covers the split-query trade-off and LC028/LC038 boundaries. FS stays `3` — `AsSplitQuery()` is a trade-off the user must own. Demoted from Medium: all named defects shipped; multi-reassignment chains stay an intentional non-goal. |
| LC007 | Database execution inside loop | Execution & Async | Warning | 4 | 4 | 4 | 4 | 5 | 5 | Low | Exceptional analyzer (all loop shapes incl. 5.5.8 deconstruction-foreach) and excellent 82-line doc; fixer conservatively rewrites only unconditional explicit-loading to `Include`. Not a `5` on Analyzer because do-while edge shapes, complex local-function chains, and conditional execution contexts remain uncovered. |
| LC008 | Synchronous EF method in async context | Execution & Async | Warning | 4 | 4 | 3 | 4 | 3 | 4 | Medium | Solid sync/async pair detection with an expression-tree-lambda guard; fixer is limited to simple 1:1 rewrites (`ToList`→`ToListAsync`) and the 35-line doc has no EF API inventory or "no async equivalent" guidance. Open deferred item: an unfixable orphaned warning fires on a sync EF call inside a `static` local function in an async method. |
| LC009 | Missing AsNoTracking in read-only path | Change Tracking & Context Lifetime | Info | 4 | 4 | 3 | 3 | 4 | 3 | Medium | Recognises `DbSet` properties and generic-repository `context.Set<T>()`; the fixer lands `AsNoTracking()` on the semantic EF source. Round-2 rejected the `Attach`/`Entry().State` FP claim (idiomatic disconnected-update pattern). Open deferred residual: cross-method mutation persisted via a helper, and no identity-resolution fixer variant. Fixer tests sparse (4). |
| LC010 | SaveChanges inside loop | Change Tracking & Context Lifetime | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | Mature loop analyzer (direct/foreach/await-foreach/do/local-function, catch-guarded retry loops) with a deliberately conservative do-while-only fixer (refuses break/continue/return/throw/yield/try and nested loops); 30 tests, 5.5.1 CRLF fixer fix in place. Exception-handler and async-iterator-defer shapes remain uncovered, matching the LC007 calibration. |
| LC011 | Entity missing primary key | Schema & Modeling | Warning | 4 | 5 | 4 | 5 | 5 | 4 | Low | Reference-quality across FP/T/DS: namespace-validated attributes, context-specific applied configurations, current-assembly config scanning, inferred owned types, scoped/chained Fluent API, duplicate-safe fixes, 46-test suite, 75-line doc. Remaining risk is exotic dynamic model construction (not source-detectable). |
| LC012 | Use ExecuteDelete instead of RemoveRange | Bulk Operations & Set-Based Writes | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Medium | Conservative analyzer with an async-aware fixer (emits `await ExecuteDeleteAsync()` in async contexts, declines rather than inject sync-over-async); 21 tests incl. nested-scope and refusal shapes; doc enumerates the change-tracker bypass and unit-of-work boundary. Open follow-up: a `SaveChanges` on a *different* context or in a mutually-exclusive branch still suppresses the report (needs same-instance + reachability analysis). |
| LC013 | Disposed context query | Change Tracking & Context Lifetime | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | Manual-only lifetime rule with strong single-assignment/conditional/coalesce/switch coverage (23 tests); rationale for no fixer is sound (deferred execution cannot be proven to materialize before disposal without whole-program analysis). Field/parameter origins intentionally not followed. |
| LC014 | Avoid string case conversion in queries | Query Shape & Translation | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | Hardened around EF-backed proof, local aliases, and `Join`/`GroupJoin` key-selector ownership. The argument walk follows only content-carrying arguments (string/reference/char), so constant-receiver calls with numeric/positional column args stay quiet while string/char-argument crimes still fire. Manual-only stance is correct (the `StringComparison` rewrite is provider/version-sensitive). |
| LC015 | Missing OrderBy before pagination | Query Shape & Translation | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | High-value reliability rule; 5.5.3 added `ElementAt`/`ElementAtOrDefault` (+async) and async `Last*` as ordering-dependent operators (`TakeLast`/`SkipLast` correctly rejected — EF can't translate them at all). Fixer registers only on a detectable single PK and bails on attribute-composite and `[Keyless]` entities. Documented residual (not source-detectable): a Fluent-only composite key can still receive a partial-key fix — the 112-line doc warns to order by every key part. Demoted from Medium: every actionable defect shipped. |
| LC016 | Avoid DateTime.Now/UtcNow in queries | Query Shape & Translation | Warning | 4 | 4 | 3 | 4 | 2 | 2 | Low | Clock detection, per-lambda dedup, and unique-naming fixer are fine (5.5.1 CRLF fix in place); 31-line doc never covers testability/mocking or modern EF constant-folding, and the impact (cacheability/testability, not correctness) caps Importance at 2. |
| LC017 | Whole entity projection | Materialization & Projection | Info | 4 | 4 | 3 | 4 | 5 | 3 | Low | Strong 38-test heuristic with a 171-line doc; FS stays `3` because the fixer rewrites to anonymous types, forcing caller refactoring rather than a safety-preserving change (a DTO-helper alternative is the obvious upgrade). |
| LC018 | FromSqlRaw with interpolated strings | Raw SQL & Security | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | Security-critical with broad call-shape coverage, an exhaustive constant-only safe-shape suite (35 tests), and `SqlQueryRaw<T>` detection on the `DatabaseFacade`. Auto-fix stays `FromSqlRaw`→`FromSqlInterpolated`; `SqlQueryRaw` is report-only. Residual follow-ups: the `SqlQueryRaw`→`SqlQuery` auto-fix is not offered, and the LC018/LC037 boundary deserves clearer doc text. Demoted from Medium: the security FN shipped (LC037 owns construction sinks since 5.5.5). |
| LC019 | Conditional Include expression | Loading & Includes | Warning | 4 | 4 | 3 | 3 | 3 | 4 | Low | Manual-only rule with sound rationale (a conditional inside an Include lambda always throws at runtime); only 10 test methods and no filtered-Include or non-EF Include negatives; doc is light on the "split query vs. project earlier vs. eager-load both branches" decision tree. |
| LC020 | Untranslatable string comparison overloads | Query Shape & Translation | Warning | 3 | 3 | 4 | 4 | 4 | 3 | Low | 5.5.2 closed the argument-flow FN (`"admin".Contains(u.Name, cmp)`); the Ordinal/OrdinalIgnoreCase FP claim was rejected on verification (default providers throw, so flagging is correct). Still not provider-aware (Npgsql `ILIKE`, opt-in Pomelo) and EF 9 `Collate()` is not addressed — both Analyzer and FP stay at `3`. |
| LC021 | IgnoreQueryFilters usage | Raw SQL & Security | Warning | 4 | 4 | 3 | 3 | 4 | 4 | Low | Narrow EF-extension detection is conservative; Round-2 rejected the EF9 selective `IgnoreQueryFilters(filterKeys)` FP claim (selective bypass still bypasses soft-delete/tenant filters). 14 tests but no `[SuppressMessage]` negatives documenting the intended suppression path. |
| LC022 | Nested collection materialization inside projection | Materialization & Projection | Info | 3 | 3 | 3 | 4 | 3 | 2 | Low | EF 9 correlated-collection translation has largely closed the gap this rule targeted; the analyzer can flag patterns EF now translates correctly, and the 43-line doc carries no EF-version caveat. Both A/FP and Importance stay pulled down. |
| LC023 | Prefer Find/FindAsync for primary key lookups | Materialization & Projection | Info | 4 | 4 | 4 | 4 | 4 | 3 | Low | Careful key/Async detection with a shipped fixer; **the Round-2 query-filter hazard is closed (2026-06-10)** — the rule now stays silent for entities with a visible `HasQueryFilter(...)` (OnModelCreating or configuration-class), because `Find`'s change-tracker hit bypasses global query filters and the rewrite could resurrect soft-deleted/other-tenant rows (verified against the EF Core 9 `EntityFinder` source: only the database fallback applies filters). The gate keys on the DbSet's entity type (so a base-class `Id` doesn't dodge it), walks base types (EF declares filters on the hierarchy root), and resolves the non-generic `Entity(typeof(X)).HasQueryFilter(...)` builder; lookalike-`HasQueryFilter` and different-entity guards are locked in; 26 tests. Residual (documented): a filter configured in another assembly is invisible, and there is still no dedicated fixer-edge test file. |
| LC024 | GroupBy with non-translatable projection | Query Shape & Translation | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | Strong fluent and query-syntax coverage (25 tests); `Any`/`All` are recognised aggregates and an aggregate whose receiver chain roots at `g` through translatable operators (`Where`/`Select`/`OrderBy`/`Distinct`) is accepted, while non-aggregate terminals still report. Manual-only is correct — the fix depends on business intent. |
| LC025 | AsNoTracking with Update/Remove | Change Tracking & Context Lifetime | Warning | 4 | 4 | 4 | 3 | 4 | 4 | Medium | Sound dataflow over nearest origins, foreach paths, and explicit `Entry.State`; honours the **last** tracking directive (5.5.6: `AsNoTracking().AsTracking()` is tracked) and stays quiet on constructed-object projections. Open deferred item: path-insensitive last-write-wins on a conditionally-reassigned local (port LC044's `HasMultipleAssignments` ambiguity guard). Edge-case tests still thin on `if (...) tracked else untracked` shapes. |
| LC026 | Missing CancellationToken in async call | Execution & Async | Info | 3 | 3 | 4 | 3 | 3 | 3 | Low | Deliberately simple pattern matcher; 5.5.13 extended token discovery to fields and readable properties (fixer passes them by name). The ambiguous-token boundary (multiple tokens in scope, naming-heuristic preference) remains a real noise source, and tests are thin on multi-token scenarios. |
| LC027 | Missing explicit foreign key property | Schema & Modeling | Info | 4 | 4 | 4 | 3 | 4 | 3 | Low | Solid modeling rule for explicit-FK teams (respects `[ForeignKey]`, owned types, conventional and configured shadow FKs; type-inferring fixer with 5.5.1 CRLF fix). 14 tests are light on inherited configurations and chained Fluent-builder patterns. |
| LC028 | Deep ThenInclude chain | Loading & Includes | Warning | 3 | 3 | 4 | 2 | 4 | 3 | Low | Configurable-depth heuristic (editorconfig `max_depth`, default 3) with only 8 test methods — no multi-Include sibling chains and no config-override tests; T drops to `2` on the harsh calibration. Manual-only stance is appropriate for a review-flag rule. |
| LC029 | Redundant identity Select | Materialization & Projection | Info | 4 | 4 | 4 | 2 | 4 | 2 | Low | Cosmetic cleanup rule; 5 test methods total and no guidance on intentional materialization-boundary uses of `Select(x => x)`. Low importance is the point — no further investment warranted. |
| LC030 | DbContext lifetime mismatch | Change Tracking & Context Lifetime | Info | 4 | 4 | 4 | 4 | 5 | 4 | Low | Strong manual-only lifetime rule: long-lived types proven via interface/base-class and DI registration shapes (BackgroundService, middleware, singletons), optional `long_lived_types` config, 22-test suite, 81-line doc explaining why no single fix exists. Severity keeps it out of the urgent stack. |
| LC031 | Unbounded query materialization | Materialization & Projection | Info | 4 | 4 | 3 | 3 | 3 | 3 | Low | Sound chain walker, correct about `Chunk` (not a bounding operator) and `TakeLast`/`SkipLast` (untranslatable, so flagging stands). Advisory-only without a fixer-rationale doc; 17 tests light on query-syntax shapes; intentional full-scan guidance thin. |
| LC032 | ExecuteUpdate for bulk scalar updates | Bulk Operations & Set-Based Writes | Info | 4 | 4 | 4 | 4 | 4 | 3 | Low | **Re-scored — the prior row was stale.** The rule now ships a full async-aware fixer (loop+`SaveChanges` → `ExecuteUpdate`, warning comment, token preservation, duplicate-write collapse, declines on observed results / value-rereads / local-source inlining) with 38 tests (24 fixer) and a 108-line doc covering the safety contract; FS/T/DS all move 3→4. Conservative declines are documented and reasonable. |
| LC033 | Use FrozenSet for static membership caches | Materialization & Projection | Info | 4 | 4 | 4 | 4 | 4 | 2 | Low | Healthy niche optimization across 7 files and 18 tests (multi-phase compilation-end analysis with strict Contains-only usage gates); low real-world impact keeps it a poor near-term investment. |
| LC034 | ExecuteSqlRaw with interpolated strings | Raw SQL & Security | Warning | 4 | 4 | 4 | 4 | 3 | 5 | Low | Strong sync/async raw-SQL detection (29 tests) with named/direct unsafe SQL, lookalike negatives, and quote-sensitive fixer suppression; doc remains light on the LC018/LC037 three-way overlap and parameterized-construction guidance. |
| LC035 | Missing Where before bulk execute | Bulk Operations & Set-Based Writes | Info | 4 | 4 | 4 | 3 | 4 | 4 | Low | High-impact safety smell; 5.5.9 closed the "base filter + optional narrowing" FP (unconditional base plus every conditional reassignment must be filtered). 18 tests still below peers on filtered-local/reassignment depth. Imp stays `4` — transactions and audit logging diminish bare data-loss risk in modern stacks. Demoted from Medium: the named FP shipped. |
| LC036 | DbContext captured by thread work item | Execution & Async | Warning | 4 | 4 | 4 | 4 | 5 | 5 | Low | High-value thread-safety rule covering lambda, anonymous-method, callback, async-lambda, member capture, factory/scope-safe, materialized-value, and local-function shapes (17 tests). The method-group FN claim was rejected — arbitrary method-group inspection is a documented non-goal. Compact 41-line doc remains a DS=5 anchor (violation, safer shape, intent, explicit non-goals). |
| LC037 | Constructed raw SQL strings | Raw SQL & Security | Warning | 4 | 4 | 4 | 4 | 3 | 5 | Low | Strong manual security rule (3-file analyzer: concatenation, `string.Format`/`Concat`, `StringBuilder`, aliased-local resolution; 26 tests); 5.5.5 added `SqlQueryRaw<T>` as a construction sink with no LC018 double-report. Docs still need concrete `StringBuilder`/`string.Format` flow examples and clearer LC018/LC034 boundary text. |
| LC038 | Excessive eager loading | Loading & Includes | Info | 3 | 4 | 4 | 3 | 3 | 2 | Low | Coarse depth-threshold heuristic (configurable, default 4) with only 6 test methods; modern EF Core split-query support reduces the underlying risk and the 33-line doc has no intentional-load rationale. |
| LC039 | Repeated SaveChanges on same context | Change Tracking & Context Lifetime | Info | 4 | 4 | 4 | 4 | 3 | 4 | Low | Useful reliability smell guarded for transaction boundaries (`using`/`await using` declarations included), mutually exclusive if/else, switch, ternary arms, and try-vs-catch saves (5.5.7); 21 tests. **DS re-scored 4→3**: the doc is 24 lines — below the calibration bar for a `4` (no intentional-use patterns, no non-goals). Remaining analyzer gaps: exception-handler and nested-loop control flow. |
| LC040 | Mixed tracking and no-tracking modes | Change Tracking & Context Lifetime | Info | 3 | 4 | 4 | 3 | 3 | 3 | Low | Branch resolution now covers ternary arms (5.5.7); exception handlers and nested scopes remain unhandled — the try/catch deferral is deliberate (a tracked entity materialised before a mid-`try` throw can still be tracked). 16 tests, no provider/transaction interaction coverage; doc silent on legitimate split-workflow rationale. |
| LC041 | Single entity over-fetches one consumed property | Materialization & Projection | Info | 4 | 4 | 3 | 2 | 2 | 2 | Low | Narrow heuristic; 5.5.12 exempted key-predicates on an upstream `Where` (same single-row-by-key fetch the terminal form exempts). Fixer intentionally excludes `FirstOrDefault`/`SingleOrDefault` to preserve no-row semantics. Deferred: hoisted `Expression<Func<>>` predicates not resolved; null-conditional single-property reads unhandled. 14 tests, 37-line doc. |
| LC042 | Complex query should be tagged | Loading & Includes | Info | 2 | 2 | 4 | 2 | 2 | 2 | Low | Crude operator-count threshold with 4 test methods; no semantic complexity distinction (`Where`+`OrderBy`+`Take` ranks the same as nested `SelectMany`+aggregates). Pure team-policy observability rule — appropriately harsh-scored, no investment warranted. |
| LC043 | Prefer await foreach over buffering async streams | Execution & Async | Info | 4 | 4 | 4 | 3 | 3 | 2 | Low | Intentionally narrow immediate-buffer-then-loop detection with proven `IAsyncEnumerable<T>` source gates; 8 test methods with no CancellationToken or buffer-argument variants (the fixer deliberately leaves token-carrying calls alone). Streaming optimization, not a correctness issue. |
| LC044 | AsNoTracking entity mutated then SaveChanges | Change Tracking & Context Lifetime | Warning | 4 | 4 | 4 | 3 | 5 | 5 | Low | Reference-quality manual rule (chain proof, context-symbol identity, single-assignment gate, block reachability, earlier-save and re-attach gates); honours the last tracking directive (5.5.6) and counts compound assignment / increment as mutations (5.5.10). 76-line doc is a DS=5 anchor. Tests stay `3` — foreach-mutation and nested-scope reachability coverage still missing. Deferred by design: a re-attach in an untaken branch suppresses (treating visible re-attach as intent is a reasonable FP trade-off). |
| LC045 | Missing Include — navigation on materialized entity | Loading & Includes | Warning | 4 | 3 | 4 | 4 | 4 | 5 | Low | **New in 5.6.0; conditional-access surface adversarially probed 2026-06-10.** Detects the canonical EF Core read-side bug: navigation access on a materialized entity without `Include` — N+1 with lazy proxies, silent null without. The dedicated post-crash adversarial pass found **no remaining crash/hang shapes** (deep `?.` chains, `!`/cast mixes, interpolation, nested conditional access in arguments, `?.` invocations all clean) and five FNs — every null-conditional spelling of already-flagged shapes (`First()?.Customer?.Name`, `First()?.Customer.Address?.City`, `First()?.Customer.Clear()`, `orders?[0].Customer`, `var o = orders?[0]`) — all fixed with regression locks (65 tests). A moves 3→4 on that evidence. FP stays `3`: `AutoInclude()` is invisible and null-guarded access deliberately fires (documented). Residual polish: parenthesized CA regrouping truncates the reported path to its head; C# 14 null-conditional assignment is a watch item for newer-Roslyn consumers. |

## Importance Ranking — what matters most to catch

This ranks rules by what a user most needs the package to catch (frequency × severity × actionability against EF Core 9+), independent of how healthy the rule currently is. Use it together with the health gaps above to plan: work flows to rules that are high on this list **and** carry gaps.

**Tier 1 — must-catch (Imp 5).** Security holes, silent data corruption, and the classic production-killers:

| Rank | Rule | Why it tops the list |
| --- | --- | --- |
| 1 | LC018 / LC034 / LC037 | SQL injection (interpolated/concatenated/constructed raw SQL). Highest severity class in the catalog; one miss is a breach. The three-rule mesh now covers `FromSqlRaw`, `ExecuteSqlRaw*`, `SqlQueryRaw<T>`, and constructed-string flows. |
| 2 | LC044 | Silent data loss: `AsNoTracking` entity mutated then `SaveChanges` — no exception, the write just never happens. Invisible at runtime until someone notices missing data. |
| 3 | LC045 | The most common real-world EF read-side bug: missing `Include` → N+1 with proxies, silent null/empty navigation without. High frequency, ships silently, surfaces as prod slowness or missing data. |
| 4 | LC007 / LC010 | N+1 query execution and per-iteration `SaveChanges` inside loops — the two classic EF performance catastrophes, both high-frequency in real codebases. |
| 5 | LC013 / LC036 | Context-lifetime correctness: querying a disposed context (guaranteed runtime crash) and DbContext captured across threads (corruption/crash under load, brutal to diagnose). |
| 6 | LC015 | Non-deterministic pagination (no `OrderBy` before `Skip`/`Take`/`ElementAt`/`Last*`) — silently wrong pages, duplicate/missing rows across requests. |

**Tier 2 — high value (Imp 4).** Real correctness/perf wins, slightly lower frequency or severity: LC002, LC004, LC006 (Cartesian explosion), LC008, LC011, LC012, LC014, LC019 (always-throws Include), LC021 (tenant/soft-delete filter bypass), LC024, LC025, LC030, LC035 (unfiltered bulk delete/update), LC039.

**Tier 3 — useful (Imp 3).** Hygiene and perf advisories: LC001, LC003, LC005, LC009, LC017, LC020, LC023, LC026, LC027, LC028, LC031, LC032, LC040.

**Tier 4 — marginal (Imp 2).** Niche, cosmetic, or superseded by modern EF: LC016, LC022 (EF 9 translates most of what it flags), LC029, LC033, LC038, LC041, LC042, LC043.

## 2026-06-10 Review — score changes and confirmations

A six-probe parallel re-audit verified every row against current source, tests (`[Fact]`/`[Theory]` counts re-counted), docs (line counts re-measured), and the 2026-06-04 rerun deltas. Changes:

| Rule | Change | Why |
| --- | --- | --- |
| LC045 | **New row**: A3 / FP3 / FS4 / T4 / DS4 / Imp5, Medium | First scorecard entry. Importance is top-tier; maturity is not — 5.6.0 shipped a compiler-killing StackOverflowException on chained `?.` (hot-fixed 5.6.1), so A/FP are capped at 3 pending an adversarial conditional-access pass. |
| LC032 | FS 3→4, T 3→4, DS 3→4 | The prior row was stale: the rule now has a full async-aware fixer (20K, declines documented), 38 tests (24 fixer — the old note claimed 14 total), and a 108-line doc with a safety contract. |
| LC023 | FS 4→3, T 4→3, Priority Low→Medium | The shipped fixer rewrites `FirstOrDefault(pk)` → `Find`, and `Find`'s change-tracker hit bypasses global query filters — on a `HasQueryFilter` entity the rewrite can return an already-tracked soft-deleted / other-tenant row the original filtered query would not have (the DB-query path applies filters, so the hazard is the tracked-instance case). Deferred since Round 2 and still unresolved; no query-filter negatives in the 16-test suite. This is the only live shipped-fix correctness hazard in the catalog. |
| LC039 | DS 4→3 | The doc is 24 lines — below this document's own calibration bar ("a 30-line doc … will not score above 3"). |
| LC028 | T 3→2 | 8 test methods, no sibling-chain or editorconfig-override coverage; consistent with LC029's T=2 at 5 methods. |
| LC006, LC015, LC018, LC035 | Priority Medium→Low | All named defects from the 2026-05-29/06-04 sweeps shipped; what remains is documented residuals or non-source-detectable gaps. |
| LC008 | Priority Low→Medium | Aligns the column with the planning shortlist (deferred orphaned-warning item is real and scoped). |

Everything else was confirmed at its existing score — including the deliberate harsh marks (LC042 A2/FP2, LC022's EF-9-driven downgrade, LC016 Imp2) and the DS=5 anchors (LC011, LC017, LC030, LC036, LC044, LC007).

## Planning Shortlist

Work flows to rules that are high in the Importance Ranking **and** carry health gaps.

| Priority | Rules | Work |
| --- | --- | --- |
| High | None | No crash, unsafe-fix-in-the-wild, or security FN is currently open. Promote on fresh concrete FP/FN/unsafe-fix/crash evidence only. |
| Medium — next batch, in order | LC012, LC025, LC009, LC008 | **LC012**: same-instance + reachability analysis for cross-context / mutually-exclusive-branch `SaveChanges`. **LC025**: port LC044's `HasMultipleAssignments` ambiguity guard for conditionally-reassigned locals. **LC009**: cross-method mutation heuristic / identity-resolution fixer variant. **LC008**: the unfixable orphaned warning on a sync EF call in a `static` local function. *(Shipped 2026-06-10: LC045's adversarial pass — no crash shapes survived, five null-conditional FNs fixed, parenthesized-regroup truncation remains a Low polish item; LC023's `HasQueryFilter` gate — the catalog's last live shipped-fix hazard is closed.)* |
| Low — opportunistic, Tier-1-importance hygiene first | LC044, LC037, LC034, LC039, LC019, LC028 | Cheap backfill on must-catch rules: LC044 foreach-mutation + nested-scope reachability tests (its only sub-4 metric); LC037/LC034 boundary docs (`StringBuilder`/`string.Format` examples, LC018/LC034/LC037 three-way split); LC039 doc expansion past 24 lines. Then LC019/LC028 test depth. Everything else is currently acceptable or appropriately harsh-scored. |

Rejected/deferred-by-design items (LC004 nested-local-function, LC036 method-group, LC040 try/catch + `Select`, LC044 untaken-branch re-attach, LC020 Ordinal flagging, LC015 `TakeLast`/`SkipLast`, LC031 `TakeLast`, LC021 selective filter keys) stay closed — do not re-chase without new evidence. See the 2026-06-04 Rerun tables below for full rationale.

## 2026-05-29 Deep Rescan

A fresh adversarial FP/FN sweep ran eight parallel `analyzer-fp-fn-hunter` probes against the highest-payoff Warning-severity rules (LC002, LC005, LC006, LC014, LC015, LC018, LC020, LC024). Every probe constructed compiling EF Core variations and reasoned from analyzer source; every finding below was **test-confirmed** against the built analyzer on net10.0. This surfaced one analyzer crash and a cluster of genuinely-new false positives, false negatives, and unsafe fixes that the 2026-05-14 audit had not captured.

| Rule | Class | Evidence (test-confirmed) | Deciding code | Status |
| --- | --- | --- | --- | --- |
| LC005 | **Crash (AD0001)** + masked FN | `from x in xs orderby a orderby b select x` threw `InvalidCastException` (`OrderingSyntax` hard-cast to `InvocationExpressionSyntax`); the reset was never reported | `MultipleOrderByAnalyzer.cs:64` | **Fixed** |
| LC014 | FP (5.4.12 regression) | Constant receiver + column in a **numeric/positional** arg fired (`"X".PadLeft(u.Age)`, `"H".Substring(0, u.Age)`) — casing never touches a column | arg-walk at `AvoidStringCaseConversionAnalyzer.cs:289-293` | **Fixed** |
| LC002 | **Unsafe fix** + FP message | `ToHashSet().ToList()` "redundant" fix rewrote to `ToList()`, silently dropping de-duplication; `ToDictionary().ToList()` / `ToLookup().ToList()` mislabeled "redundant" | `PrematureMaterializationMethodRules.cs:97-106` | **Fixed** (unsafe fix immediately; message in 5.5.4) |
| LC006 | FP (+ FN) | `var q = db.Users.AsSplitQuery(); q.Include(a).Include(b)` fired despite an effective split (walker stopped at `ILocalReferenceOperation`) | `CartesianExplosionChainAnalysis.cs:57-65` | **Fixed** (`LocalAssignmentCache`) |
| LC024 | FP ×3 | `g.Any()`, `g.Where(p).Count()`, `g.Select(s).Sum()` all flagged though EF Core 9 translates them | `GroupByNonTranslatableAnalyzer.cs:191, 203-204, 137` | **Fixed** (chain walk + `Any`/`All`) |
| LC015 | **Unsafe fix** + FN | Fixer offered partial-key `OrderBy(x => x.Id)` on a `[Keyless]` entity; `ElementAt`/async `Last*` never flagged | `MissingOrderByFixer.cs:61-113` | **Fixed** (`[Keyless]` bail; operators in 5.5.3); Fluent-composite-key documented |
| LC020 | FP + FN | Argument-derived crime missed (`"admin".Contains(u.Name, …)`); Ordinal FP claim later rejected | `StringContainsWithComparisonAnalyzer.cs:50, 55, 164-166` | **FN fixed in 5.5.2; FP claim rejected** |
| LC018 | **FN (security)** | `db.Database.SqlQueryRaw<T>($"… {id}")` and concat/format equivalents were invisible to the entire Raw SQL neighborhood | `AvoidFromSqlRawWithInterpolationAnalyzer.cs:43,49` | **Fixed** (LC018 detection; LC037 construction sinks in 5.5.5) |

## 2026-06-04 Rerun

An overnight hardening run cleared the 2026-05-29 shortlist and then ran a **fresh four-probe `analyzer-fp-fn-hunter` rescan** over twelve Warning- and high-value-Info rules not covered by the 2026-05-29 sweep (LC004, LC007, LC008, LC012, LC013, LC025, LC034, LC035, LC036, LC039, LC040, LC044). Every finding was **independently re-verified** against the built analyzer on net10.0 before any fix shipped, and several probe claims were **rejected on verification**. Thirteen releases shipped (`5.5.1` … `5.5.13`, the last three from a second three-probe rescan over LC001, LC003, LC009, LC019, LC021, LC023, LC026, LC031, LC041), plus a dev-tooling fix.

### Shipped

| Release | Rule(s) | Class | What shipped |
| --- | --- | --- | --- |
| 5.5.1 | LC010, LC011, LC016, LC027 | Fixer line-endings | Fixers harvested the document's own end-of-line trivia instead of hard-coded `LF`, fixing 11 failing fixer tests on CRLF (Windows) checkouts. |
| 5.5.2 | LC020 | FN | Column flowing through a method **argument** (`"admin".Contains(u.Name, cmp)`) now flagged; the Ordinal/OrdinalIgnoreCase FP claim was rejected (default providers throw). |
| 5.5.3 | LC015 | FN | `ElementAt`/`ElementAtOrDefault` (+async) and async `Last*` flagged as ordering-dependent; `TakeLast`/`SkipLast` rejected (untranslatable). |
| 5.5.4 | LC002 | Message | Keyed (`ToDictionary`) / grouped (`ToLookup`) sources no longer reported as redundant — a shape change, not a redundant re-materialization. |
| 5.5.5 | LC037 (LC018 boundary) | Security FN | `SqlQueryRaw<T>` is now an LC037 construction sink (`string.Format`/`Concat`/`StringBuilder`/aliased-local); LC018 keeps interpolation/concat, no double-report. |
| 5.5.6 | LC025, LC044 | FP | Both honour the **last** tracking directive: `AsNoTracking().AsTracking()` is tracked and no longer fires. |
| 5.5.7 | LC039, LC040 | FP | `AreMutuallyExclusiveBranches` recognises ternary arms (LC040) and try-vs-`catch` saves (LC039). |
| 5.5.8 | LC007 | FN | Deconstruction `foreach (var (a, b) in xs)` analysed via the shared `CommonForEachStatementSyntax` base. |
| 5.5.9 | LC035 | FP | "Base filter + optional narrowing" no longer fires; the unconditional base plus every later conditional reassignment must be filtered. |
| 5.5.10 | LC004, LC044 | FN | LC004 follows a C# query expression to its source parameter; LC044 treats compound assignment (`+=`) and increment (`++`) as mutations. |
| 5.5.11 | LC001 | FN | Local methods inside `Sum`/`Average`/`Min`/`Max` selectors now flagged. |
| 5.5.12 | LC041 | FP | Key-predicate on an upstream `Where` (`Where(x => x.Id == id).First()`) exempt — same single-row-by-key fetch the terminal form exempts. |
| 5.5.13 | LC026 | FN | A `CancellationToken` in a field or readable property counts as available (fixer passes it by name). |
| (tooling) | — | — | `RuleCatalogDocGenerator --check` made line-ending-robust. |

### Rejected or deferred on verification (skeptic notes)

These claims were **not** acted on — do not re-chase without new evidence:

| Rule | Claim | Verdict |
| --- | --- | --- |
| LC020 | Ordinal/OrdinalIgnoreCase over-flagged (FP) | **Rejected.** Default relational providers throw on `StringComparison` overloads; only Npgsql/opt-in Pomelo translate them. The rule stays provider-agnostic. |
| LC015 | `TakeLast`/`SkipLast` not flagged (FN) | **Rejected.** EF Core cannot translate them at all (dotnet/efcore#25242, #17065) — "add OrderBy" is wrong advice. |
| LC036 | Instance method-group passed to `Task.Run` (FN) | **Rejected.** Arbitrary method-group inspection is a documented non-goal. |
| LC031 | `TakeLast(n)` flagged as unbounded (FP) | **Rejected.** Untranslatable; suppressing would imply a safe bounded query that actually throws. |
| LC021 | EF9 selective `IgnoreQueryFilters(filterKeys)` flagged | **Rejected.** Selective disabling still bypasses those soft-delete/tenant filters. |
| LC009 | `Attach` / `Entry().State = Modified` read flagged (FP) | **Rejected.** `AsNoTracking()` before an explicit-state-set is the idiomatic disconnected-update pattern. |
| LC003 | Existence-check edge cases | **No findings — robust.** |
| LC004 | Parameter consumed only inside a nested local function (FN) | **Deferred.** Deliberate FP-avoidance for un-invoked lambdas. |
| LC008 | Sync EF in a `static` local function inside an async method (FP) | **Deferred → now Medium.** The unfixable orphaned-warning sub-case is the scoped follow-up. |
| LC040 | try/catch branches (FP); `Select` short-circuit (FN) | **Deferred.** The try/catch case is debatable; the `Select` bail correctly avoids scalar-projection noise. |
| LC025 | Path-insensitive last-write-wins on a conditionally-reassigned local | **Deferred → Medium.** Port LC044's `HasMultipleAssignments` guard. |
| LC044 | Re-attach inside an untaken branch suppresses (FN) | **Deferred.** Treating visible re-attach as intent is a reasonable trade-off. |
| LC012 | SaveChanges on a *different* context / mutually-exclusive branch suppresses (FN) | **Open follow-up → Medium.** Needs same-instance + reachability analysis. |
| LC023 | `FirstOrDefault(pk)` → `Find` changes results under a global query filter | **Deferred → Medium (top of the verify queue).** Narrowed on review: `Find` applies query filters on its database query but returns an already-tracked filtered-out instance from the change tracker; confirm against EF Core 9 then gate on `HasQueryFilter`. |
| LC009 | Tracked mutation persisted via a helper / cross-method SaveChanges (FP) | **Deferred → Medium.** A "property mutation of a materialized entity ⇒ write path" heuristic would close the common shape. |
| LC041 | Hoisted `Expression<Func<>>` predicate (FP); null-guarded single-property read (FN) | **Deferred.** Two narrow LC041 gaps; low importance. |

## Verification Baseline

Package version: **5.6.1**

Base audited commit: master at `ae15734` (5.6.1 release merge). Since the 2026-06-04 baseline (5.5.13): descriptor hygiene (helpLinkUri on all rules, sealed/FixAll architecture tests), repo/CI hardening, the `IncludePathParser` extraction shared by LC006/LC045, **LC045 shipped in 5.6.0** (four pre-ship review-hardening rounds), and the **5.6.1 hot-fix** for the LC045 chained-`?.` StackOverflowException that killed csc on 5.6.0.

Architecture tests enforce the rule quality contract for public package metadata, code-fix provider exports, documentation drift, repository layout, and `samples/LinqContraband.Sample/sample-diagnostics.json` sample expectations.

Current local verification (2026-06-10, net10.0):

- `dotnet test LinqContraband.sln --framework net10.0` passed with **1006 tests** (was 919 at the 2026-06-04 baseline; the bulk of the increase is the LC045 suite incl. post-crash regression tests).
- The six-probe re-audit re-counted `[Fact]`/`[Theory]` methods per rule, re-measured doc line counts, and read each analyzer/fixer source; the LC032 fixer existence, LC023 fixer existence, and LC039 doc length were verified directly (they drove the score changes above).
- `dotnet --list-runtimes` shows only .NET 10 runtimes locally; full multi-target verification (net8/net9) remains delegated to the GitHub `dotnet.yml` CI and the `publish.yml` workflow.

Historical baselines: 2026-06-04 rerun verified 919 tests at 5.5.13; 2026-05-29 deep rescan verified 828 tests at 5.4.12 (840d00b); the 2026-05-14 fine-comb re-audit (six parallel slices, scores moved on 30 of 44 rules) established the harsh calibration and the DS=5 anchors (LC011 FP/T/DS, LC030 DS, LC036 DS/Imp) that remain the reference for what a `5` requires.
