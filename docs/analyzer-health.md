# Analyzer Health

Reviewed: 2026-07-03 (LC037 StringBuilder statement-flow fix; prior: 2026-07-02 multi-agent adversarial review — 45 parallel per-rule reviewers + skeptic verification + 7 worktree-isolated net10.0 test-confirmations; **7 defects test-confirmed**, see the 2026-07-02 section; repo hygiene pass 2026-06-26, full per-rule re-verification 2026-06-10, overnight rerun 2026-06-04, deep rescan 2026-05-29)

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

> Rows below are the **2026-06-10 re-verified state** — the 2026-06-04 rerun deltas (5.5.1–5.5.13) and the 5.6.0/5.6.1 LC045 releases are folded into the rows. Every row was independently re-checked against current source, tests, and docs by a six-probe parallel audit; subsequent hardening has raised the local net10.0 suite to **1159 tests**.

| Rule | Title | Domain | Severity | Analyzer | False Positives | Fix Strategy | Tests | Docs/Samples | Importance | Priority | Notes |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| LC001 | Local method usage in IQueryable | Query Shape & Translation | Warning | 4 | 3 | 3 | 4 | 4 | 3 | Low | Detection is sound and includes aggregate selectors (`Sum`/`Average`/`Min`/`Max` selector FN fixed in 5.5.11). Fixer now handles extension syntax and static `Queryable` syntax, including aliases, reordered named `source:`/`outer:` arguments, ordered continuations, extension/static ordered source chains, and nested static continuations, by making the client-evaluation boundary explicit with `AsEnumerable()`/`Enumerable`, with semantic guards for receivers named `Queryable`, static wrappers such as `Queryable.AsQueryable`, upstream static operators that should remain server-side, and complex source expressions that need parentheses before `.AsEnumerable()`, 16 fixer tests, and 37 LC001 tests overall. Docs now explain parameterized constants, SQL-translatable alternatives, mapped functions/projectables, and the client-eval trade-off. Modern EF partial client evaluation keeps urgency moderate. |
| LC002 | Premature query continuation after materialization | Materialization & Projection | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | Strong analyzer/fixer pair (45 tests, 14 fixer). De-duplicating set sources (`ToHashSet().ToList()`) and keyed/grouped sources (`ToDictionary().ToList()`, 5.5.4) are no longer treated as redundant, closing both the unsafe fix and the misleading message. Docs and sample now cover `ToList()`/`ToArray()`/`AsEnumerable()` boundary variance, provider-safe lambda gates, intentional client-boundary patterns, direct redundant pairs, shape-changing materializers, and the narrow fixer contract. |
| LC003 | Prefer Any() over Count() existence checks | Materialization & Projection | Warning | 3 | 4 | 4 | 4 | 4 | 3 | Low | **2026-07-02 constant-zero fixer guard shipped:** `Count() == Empty` where `const int Empty = 0` now rewrites to `!Any()` instead of silently inverting to `Any()`. The fixer asks the semantic model for folded zero constants, matching the analyzer's existing constant-value proof while preserving bare literal behaviour, async replacements, predicate preservation, and scalar-context coverage (boolean assignments, return expressions, async `LongCountAsync`→`AnyAsync`; 30 LC003 tests overall, 9 fixer tests). Analyzer stays 3 because the rule is intentionally limited to direct binary comparisons rather than deeper dataflow. |
| LC004 | IQueryable passed as IEnumerable | Query Shape & Translation | Warning | 4 | 4 | 3 | 4 | 4 | 4 | Low | Solid API-boundary analyzer proving foreach/forwarding/materializing sinks; 5.5.10 closed the query-expression FN (a C# query expression is now followed to its source parameter). Docs now cover explicit materialization vs `IQueryable` signatures, forwarding chains, expression-bodied/query-syntax consumption, safe deferred boundaries, source-body limits, nested local-function/lambda scoping, and the narrow `.ToList()` fixer contract. Nested-local-function skipping stays a deliberate FP-avoidance non-goal. |
| LC005 | Multiple OrderBy calls | Query Shape & Translation | Warning | 4 | 4 | 4 | 3 | 4 | 3 | Low | Linear chain heuristic with a safe `ThenBy` fixer; the query-comprehension crash (`orderby a orderby b` → AD0001) is fixed and reported at the `orderby` clause (report-only). Single-assignment sorted locals are now followed, so `var sorted = q.OrderBy(...); sorted.OrderBy(...)` reports, including parenthesized initializers, while reassigned locals, deconstruction writes, and `out`/`ref` writes stay quiet. The fixer is offered only when the receiver still has an ordered type, including static `Enumerable`/`Queryable` syntax; widened `IEnumerable<T>`/`IQueryable<T>` locals report as manual fixes. Tests are still modest (22 methods, 5 fixer), and chains broken across intervening operators remain a deliberate non-goal. |
| LC006 | Multiple collection Includes | Loading & Includes | Warning | 4 | 4 | 3 | 4 | 4 | 4 | Low | High-impact rule with a sibling-collection precision contract; `LocalAssignmentCache` follows single-assignment locals (closing both the `AsSplitQuery()`-across-a-local FP and the split-sibling FN). Include-path parsing now lives in the shared `IncludePathParser` (5.6.0 refactor, shared with LC045). 94-line doc covers the split-query trade-off and LC028/LC038 boundaries. FS stays `3` — `AsSplitQuery()` is a trade-off the user must own. Demoted from Medium: all named defects shipped; multi-reassignment chains stay an intentional non-goal. |
| LC007 | Database execution inside loop | Execution & Async | Warning | 4 | 4 | 4 | 4 | 5 | 5 | Low | Exceptional analyzer (all loop shapes incl. 5.5.8 deconstruction-foreach) and excellent 82-line doc; fixer conservatively rewrites only unconditional explicit-loading to `Include`. Not a `5` on Analyzer because do-while edge shapes, complex local-function chains, and conditional execution contexts remain uncovered. |
| LC008 | Synchronous EF method in async context | Execution & Async | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **2026-07-02 awaited-receiver fixer guard shipped:** when the sync invocation is the receiver of a following member, element, invocation, or null-conditional access, the fixer now parenthesizes the awaited async call so `db.Users.ToList().Count` becomes `(await db.Users.ToListAsync()).Count` and `db.Users.FirstOrDefault()?.Name` becomes `(await db.Users.FirstOrDefaultAsync())?.Name` instead of binding the continuation to the `Task`. Analyzer detection, the expression-tree-lambda guard, static-local-function sync boundary, and existing async-counterpart families stay unchanged; LC008 now has 27 tests overall, including 9 fixer tests. |
| LC009 | Missing AsNoTracking in read-only path | Change Tracking & Context Lifetime | Info | 4 | 4 | 3 | 4 | 4 | 3 | Low | Recognises `DbSet` properties and generic-repository `context.Set<T>()`; the fixer lands `AsNoTracking()` on the semantic EF source. **The deferred cross-method residual is closed (2026-06-10)**: a property mutation of the materialized result — on the result local, a foreach variable over it, or inline on the materializer (compound/increment included) — now marks the body as a write path, so the helper-commits-the-save shape no longer gets misadvised; DTO-mutation and read-only-access guards keep the rule firing. Remaining residuals: an entity returned untouched and mutated by a caller stays invisible (documented, why severity is Info), and the identity-resolution fixer variant is still not offered (FS stays `3`). |
| LC010 | SaveChanges inside loop | Change Tracking & Context Lifetime | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | Mature loop analyzer (direct/foreach/await-foreach/do/local-function, catch-guarded retry loops) with a deliberately conservative do-while-only fixer (refuses break/continue/return/throw/yield/try and nested loops); 30 tests, 5.5.1 CRLF fixer fix in place. Exception-handler and async-iterator-defer shapes remain uncovered, matching the LC007 calibration. |
| LC011 | Entity missing primary key | Schema & Modeling | Warning | 4 | 5 | 4 | 5 | 5 | 4 | Low | **2026-07-02 builder-local recursion guard shipped:** a malformed self-referential builder local in `OnModelCreating` (for example `var entity = entity.HasKey("Id");`) no longer recurses through `TryResolveLocalBuilder` until StackOverflow aborts the analysis host. Builder-expression resolution now carries a visited-expression set, so incomplete/non-compiling IDE edits are treated as unresolved while namespace-validated attributes, context-specific applied configurations, current-assembly config scanning, inferred owned types, scoped/chained Fluent API, duplicate-safe fixes, and valid local-builder resolution remain intact; 47 LC011 tests pass on net10.0. |
| LC012 | Use ExecuteDelete instead of RemoveRange | Bulk Operations & Set-Based Writes | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | Conservative analyzer with an async-aware fixer (emits `await ExecuteDeleteAsync()` in async contexts, declines rather than inject sync-over-async); doc enumerates the change-tracker bypass and unit-of-work boundary. **The Round-2 open follow-up is closed (2026-06-10)**: a later `SaveChanges` now only suppresses when it can actually commit the removals — saves on a provably different context (both receivers resolve through single-assignment alias chains to different freshly-created locals) and saves in mutually exclusive if/else or switch branches no longer mask the report, while aliases, parameters/fields/factory results, try/catch shapes, and `goto case` switches stay conservatively quiet; 30 tests. |
| LC013 | Disposed context query | Change Tracking & Context Lifetime | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | Manual-only lifetime rule with strong single-assignment/conditional/coalesce/switch coverage (23 tests); rationale for no fixer is sound (deferred execution cannot be proven to materialize before disposal without whole-program analysis). Field/parameter origins intentionally not followed. |
| LC014 | Avoid string case conversion in queries | Query Shape & Translation | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **2026-07-02 EF async-terminal pass shipped:** EF's async predicate terminals on `EntityFrameworkQueryableExtensions` (`AnyAsync`/`AllAsync`/`CountAsync`/`FirstOrDefaultAsync`/`SingleAsync`/…) now receive the same case-conversion diagnostics as synchronous `Queryable` predicates, closing the dominant modern EF style asymmetry. EF-backed proof, local aliases, `Join`/`GroupJoin` key-selector ownership, content-carrying argument walks, and the manual-only remediation stance stay unchanged; 26 LC014 tests pass on net10.0. |
| LC015 | Missing OrderBy before pagination | Query Shape & Translation | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | High-value reliability rule; 5.5.3 added `ElementAt`/`ElementAtOrDefault` (+async) and async `Last*` as ordering-dependent operators (`TakeLast`/`SkipLast` correctly rejected — EF can't translate them at all). Fixer registers only on a detectable single PK and bails on attribute-composite and `[Keyless]` entities. Documented residual (not source-detectable): a Fluent-only composite key can still receive a partial-key fix — the 112-line doc warns to order by every key part. Demoted from Medium: every actionable defect shipped. |
| LC016 | Avoid DateTime.Now/UtcNow in queries | Query Shape & Translation | Warning | 4 | 4 | 3 | 4 | 4 | 2 | Low | Clock detection, per-lambda dedup, and unique-naming fixer are fine (5.5.1 CRLF fix in place). Docs/README/sample now explain deterministic clock boundaries, injected-clock testability, `UtcNow` timestamp preference, provider-specific server-clock alternatives, and why the fixer stays limited to local extraction. Importance stays capped at 2 because this is cacheability/testability guidance, not a correctness rule. |
| LC017 | Whole entity projection | Materialization & Projection | Info | 4 | 4 | 3 | 4 | 5 | 3 | Low | Strong 37-test heuristic with a 176-line doc; FS stays `3` because the fixer rewrites to anonymous types, forcing caller refactoring rather than a safety-preserving change (a DTO-helper alternative is the obvious upgrade). |
| LC018 | FromSqlRaw with interpolated strings | Raw SQL & Security | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | Security-critical with broad call-shape coverage, an exhaustive constant-only safe-shape suite, and `SqlQueryRaw<T>` detection on the `DatabaseFacade`. **The `SqlQueryRaw<T>` fixer residual is closed (2026-06-26)**: direct interpolated scalar/keyless query SQL now rewrites to `SqlQuery<T>` while preserving generic type arguments. Fix strategy stays `4` because concatenation, raw-parameter calls, and quoted interpolation holes remain intentionally manual. Docs/README/sample now spell out the `FromSqlRaw`/`SqlQueryRaw<T>` safe-sibling split and the LC037 constructed-SQL boundary. Demoted from Medium: the security FN shipped (LC037 owns construction sinks since 5.5.5). |
| LC019 | Conditional Include expression | Loading & Includes | Warning | 4 | 4 | 3 | 4 | 4 | 4 | Low | Manual-only rule with sound rationale (conditional Include/ThenInclude navigation choices fail at runtime). Coverage now includes 13 tests: root and receiver conditionals, coalesce, collection ThenInclude, filtered-Include predicate/order/take negatives, and non-EF Include/ThenInclude lookalikes. Docs now cover split-query, projection, eager-load-both, branch-specific filtered Include, and the filtered-Include boundary. |
| LC020 | Untranslatable string comparison overloads | Query Shape & Translation | Warning | 3 | 3 | 4 | 4 | 4 | 3 | Low | 5.5.2 closed the argument-flow FN (`"admin".Contains(u.Name, cmp)`); the Ordinal/OrdinalIgnoreCase FP claim was rejected on verification (default providers throw, so flagging is correct). Still not provider-aware (Npgsql `ILIKE`, opt-in Pomelo) and EF 9 `Collate()` is not addressed — both Analyzer and FP stay at `3`. |
| LC021 | IgnoreQueryFilters usage | Raw SQL & Security | Warning | 4 | 4 | 3 | 4 | 4 | 4 | Low | Narrow EF-extension detection is conservative; Round-2 rejected the EF9 selective `IgnoreQueryFilters(filterKeys)` FP claim (selective bypass still bypasses soft-delete/tenant filters). Coverage now includes 18 tests spanning extension/static call diagnostics, EF/lookalike and `IEnumerable` negatives, local pragma suppression, method/type `SuppressMessage`, static-call pragma suppression, `.editorconfig` severity suppression, and generated-code exclusion. |
| LC022 | Nested collection materialization inside projection | Materialization & Projection | Info | 3 | 3 | 3 | 4 | 4 | 2 | Low | EF 9 correlated-collection translation has largely closed the gap this rule targeted, so the analyzer can flag patterns EF now translates correctly. Docs/README/sample now frame LC022 as an advisory query-shape review, explain modern EF correlated-collection translation, document split/direct-projection/DTO-contract choices, and spell out the conservative fixer contract. Both A/FP and Importance stay pulled down. |
| LC023 | Prefer Find/FindAsync for primary key lookups | Materialization & Projection | Info | 4 | 4 | 4 | 4 | 4 | 3 | Low | Careful key/Async detection with a shipped fixer; **the Round-2 query-filter hazard is closed (2026-06-10)** — the rule now stays silent for entities with a visible `HasQueryFilter(...)` (OnModelCreating or configuration-class), because `Find`'s change-tracker hit bypasses global query filters and the rewrite could resurrect soft-deleted/other-tenant rows (verified against the EF Core 9 `EntityFinder` source: only the database fallback applies filters). The gate keys on the DbSet's entity type (so a base-class `Id` doesn't dodge it), walks base types (EF declares filters on the hierarchy root), and resolves the non-generic `Entity(typeof(X)).HasQueryFilter(...)` builder; lookalike-`HasQueryFilter` and different-entity guards are locked in; 24 tests. Residual (documented): a filter configured in another assembly is invisible, and there is still no dedicated fixer-edge test file. |
| LC024 | GroupBy with non-translatable projection | Query Shape & Translation | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | Strong fluent and query-syntax coverage (25 tests); `Any`/`All` are recognised aggregates and an aggregate whose receiver chain roots at `g` through translatable operators (`Where`/`Select`/`OrderBy`/`Distinct`) is accepted, while non-aggregate terminals still report. Manual-only is correct — the fix depends on business intent. |
| LC025 | AsNoTracking with Update/Remove | Change Tracking & Context Lifetime | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | Sound dataflow over nearest origins, foreach paths, and explicit `Entry.State`; honours the **last** tracking directive (5.5.6) and stays quiet on constructed-object projections. **The deferred path-insensitivity item is closed (2026-06-10)**: when the latest origin before the write is conditional relative to the use and the latest *unconditional fallback* disagrees on tracking-ness (superseded history doesn't count), the verdict is path-dependent and the rule stays quiet — while unconditional latest origins, agreeing fallbacks, and same-branch reassign+write shapes all keep firing (locked in by 6 new tests; 29 total). T moves 3→4 with the conditional-reassignment family covered. |
| LC026 | Missing CancellationToken in async call | Execution & Async | Info | 3 | 3 | 4 | 4 | 4 | 3 | Low | **2026-07-02 chained-query fixer guard shipped:** the fixer now targets the invocation covered by the diagnostic span, so `query.Where(...).ToListAsync()` receives the token on `ToListAsync(cancellationToken)` instead of appending it to the inner `Where(...)`. Token discovery (fields/properties, `ct` preference), default/`CancellationToken.None` replacement, and named-argument preservation stay unchanged; 22 LC026 tests pass on net10.0. Analyzer/FP stay 3 because the rule deliberately avoids inferring business intent between domain-specific tokens. |
| LC027 | Missing explicit foreign key property | Schema & Modeling | Info | 4 | 4 | 4 | 3 | 4 | 3 | Low | Solid modeling rule for explicit-FK teams (respects `[ForeignKey]`, owned types, conventional FKs, configured shadow FKs, and single-assignment relationship-builder locals; type-inferring fixer with 5.5.1 CRLF fix). 27 tests cover direct Fluent configuration, configuration classes, string shadow FKs, relationship-builder locals, reused local names in separate scopes, receiver-chain completion including split `WithOne(...)`, nested lambda shadowing, shadowed non-builder locals, shadowed parameters, out-var and foreach shadows, same-name member assignments, and reassigned-builder ambiguity including deconstruction and `ref` writes. Inherited configuration patterns remain light. |
| LC028 | Deep ThenInclude chain | Loading & Includes | Warning | 3 | 3 | 4 | 3 | 4 | 3 | Low | Configurable-depth heuristic (editorconfig `max_depth`, default 3) with 11 test methods. Coverage now locks configured-threshold overrides, invalid-config fallback to the default threshold, sibling include-chain depth resets, and per-chain reporting when multiple sibling chains exceed the threshold. Manual-only stance is appropriate for a review-flag rule. |
| LC029 | Redundant identity Select | Materialization & Projection | Info | 4 | 4 | 4 | 3 | 4 | 2 | Low | Cosmetic cleanup rule; now covers statement-bodied interface-enumerable identity projections such as `items.Select(x => { return x; })`, keeps the fixer boundary-preserving for explicit receivers such as `AsEnumerable()`, preserves parenthesized/cast/null-forgiving fluent receivers, and skips static, concrete-enumerable, awaited-task, explicit-cast, and type-changing projection forms that the fluent fixer cannot safely rewrite. Docs clarify that intentional client/materialization boundaries should be expressed directly rather than with `Select(x => x)`. 16 tests. Low importance is the point — no further investment warranted. |
| LC030 | DbContext lifetime mismatch | Change Tracking & Context Lifetime | Info | 4 | 4 | 4 | 4 | 5 | 4 | Low | Strong manual-only lifetime rule: long-lived types proven via interface/base-class and DI registration shapes (BackgroundService, middleware, singletons), optional `long_lived_types` config, 22-test suite, 81-line doc explaining why no single fix exists. Severity keeps it out of the urgent stack. |
| LC031 | Unbounded query materialization | Materialization & Projection | Info | 4 | 4 | 4 | 4 | 4 | 3 | Low | Sound chain walker, correct about `Chunk` (not a bounding operator) and `TakeLast`/`SkipLast` (untranslatable, so flagging stands). Manual-only rationale now explains that pagination, keyset/cursor paging, exports, streaming/batching, and reviewed suppressions are product decisions the analyzer cannot choose. Coverage now includes query syntax over `DbContext.Set`, bounded query-syntax aliases, `Skip` without `Take`, `TakeLast`, transparent query options, and 22 LC031 tests. Docs/README/sample spell out non-bounds and intentional full-scan handling. |
| LC032 | ExecuteUpdate for bulk scalar updates | Bulk Operations & Set-Based Writes | Info | 4 | 4 | 4 | 4 | 4 | 3 | Low | **2026-07-02 unsupported-receiver fixer guard shipped:** receiver chains containing `Skip`, `Take`, or `Distinct` still report LC032 but no longer offer a code fix, because EF cannot translate those operators as part of an `ExecuteUpdate` receiver. The guard covers fluent chains and static `Queryable` source arguments; the async-aware fixer remains available for proven direct/filter/order chains, preserves warning comment, token propagation, and duplicate-write collapse, and now has 40 tests overall including 26 fixer tests. |
| LC033 | Use FrozenSet for static membership caches | Materialization & Projection | Info | 4 | 4 | 4 | 4 | 4 | 2 | Low | Healthy niche optimization across 7 files and 18 tests (multi-phase compilation-end analysis with strict Contains-only usage gates); low real-world impact keeps it a poor near-term investment. |
| LC034 | ExecuteSqlRaw with interpolated strings | Raw SQL & Security | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | Strong sync/async raw-SQL detection (29 tests) with named/direct unsafe SQL, lookalike negatives, and quote-sensitive fixer suppression. Docs now spell out LC018/LC034/LC037 ownership, direct-vs-hidden construction, quoted-interpolation limits, and parameterized `ExecuteSqlRaw` alternatives. |
| LC035 | Missing Where before bulk execute | Bulk Operations & Set-Based Writes | Info | 4 | 4 | 4 | 4 | 4 | 4 | Low | High-impact safety smell; 5.5.9 closed the "base filter + optional narrowing" FP and 5.6.14 locks the surrounding reassignment depth: overwritten earlier unfiltered assignments, filtered-local conditional reassignments, multiple optional filtered narrowings, and unfiltered catch-path reassignments. 22 tests. Imp stays `4` because transactions and audit logging diminish bare data-loss risk in modern stacks. Demoted from Medium: the named FP shipped. |
| LC036 | DbContext captured by thread work item | Execution & Async | Warning | 4 | 4 | 4 | 4 | 5 | 5 | Low | High-value thread-safety rule covering lambda, anonymous-method, callback, async-lambda, member capture, factory/scope-safe, materialized-value, and local-function shapes (17 tests). The method-group FN claim was rejected — arbitrary method-group inspection is a documented non-goal. Compact 41-line doc remains a DS=5 anchor (violation, safer shape, intent, explicit non-goals). |
| LC037 | Constructed raw SQL strings | Raw SQL & Security | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | Strong manual security rule (3-file analyzer: concatenation, `string.Format`/`Concat`, fluent and statement-based `StringBuilder`, aliased-local resolution; 83 tests); 5.5.5 added `SqlQueryRaw<T>` as a construction sink with no LC018 double-report. **2026-07-02 recursion guard shipped:** the valid `sql = sql + id; ExecuteSqlRaw(sql)` self-reference now reports normally instead of driving `TryResolveLocalValue` into an unbounded StackOverflow. **2026-07-03 statement-flow pass:** separate `builder.Append(...)` statements before `builder.ToString()` now report when a non-constant append can still flow into the raw SQL string, including null-conditional appends, local dynamic append values, method-call append values, loop-carried and compound-assigned append-local writes, caught-throw continuations with exact, alias, ordinary base, user-defined base, and framework base exception catches, fluent `builder.Clear().Append(...)` chains, builder-local aliases, self-preserving assignments, conditional builder value writes, conditional alias writes, copied builder expressions, and constructor copies from tainted builders, while constant-only builders, branch-selected literal append locals, path-dominated constant append-local overwrites, per-iteration constant append-local resets, variable-capacity constructors, constant compound assignments, terminating-branch local writes, alias reassignments, maybe-reassigned alias clears, short-circuit clears, short-circuit assignment resets, loop-guarded branch clears, same-loop branch exits, try/catch-contained branch clears, catch-exiting throws, catch ordering and constant-false filters that make the sink unreachable, guaranteed `finally` clears, conditional `finally` clears, early-return/loop/switch/nested terminating guard paths, only-surviving-branch clears, and guaranteed fluent or direct `Clear()` resets stay conservative. Local resolution only considers writes that completed before the value currently being inspected, and recursive local/string-builder chasing is capped, preserving prior alias/conditional-write behaviour while preventing compiler/IDE aborts. |
| LC038 | Excessive eager loading | Loading & Includes | Info | 3 | 4 | 4 | 4 | 4 | 2 | Low | Coarse but documented depth-threshold heuristic (configurable, default 4) with 10 test methods covering default/suppressing/lowering/invalid thresholds, `DbContext.Set`, transparent LINQ/EF query options, and non-EF Include lookalikes. Docs now frame the warning as a manual review prompt, explain intentional full-aggregate loads, projection/separate-query alternatives, split-query limits, and the LC006 cartesian-explosion boundary. |
| LC039 | Repeated SaveChanges on same context | Change Tracking & Context Lifetime | Info | 4 | 4 | 4 | 4 | 4 | 4 | Low | Useful reliability smell guarded for transaction boundaries (`using`/`await using` declarations included), mutually exclusive if/else, switch, ternary arms, and try-vs-catch saves (5.5.7); 21 tests. Docs now cover batching guidance, explicit transaction boundaries, branch/try/finally behavior, separate contexts, executable-root scoping, EF-only boundary recognition, and the manual-only rationale. Remaining analyzer gaps: exception-handler and nested-loop control flow. |
| LC040 | Mixed tracking and no-tracking modes | Change Tracking & Context Lifetime | Info | 3 | 4 | 4 | 4 | 4 | 3 | Low | Branch resolution now covers ternary arms (5.5.7); exception handlers and nested scopes remain unhandled — the try/catch deferral is deliberate (a tracked entity materialised before a mid-`try` throw can still be tracked). Coverage now locks transparent EF query options (`AsSplitQuery()`/`TagWith(...)`) and explicit transactions, bringing LC040 to 18 tests. Docs/README/sample now explain legitimate split workflows, separate scopes/contexts, why transactions do not change tracking mode, and the manual-only fixer rationale. |
| LC041 | Single entity over-fetches one consumed property | Materialization & Projection | Info | 4 | 4 | 3 | 3 | 3 | 2 | Low | Narrow heuristic; 5.5.12 exempted key-predicates on an upstream `Where` (same single-row-by-key fetch the terminal form exempts). Null-conditional single-property reads, scalar chains, and method chains such as `user?.Name`, `user?.Name.Length`, and `user?.Name.Trim()` now count as the same one-property over-fetch and remain diagnostic-only so the fixer does not leave stale conditional-access syntax behind. Fixer also intentionally excludes `FirstOrDefault`/`SingleOrDefault` to preserve no-row semantics. Remaining residual: hoisted `Expression<Func<>>` predicates are not resolved. 21 tests. |
| LC042 | Complex query should be tagged | Loading & Includes | Info | 2 | 2 | 4 | 2 | 2 | 2 | Low | Crude operator-count threshold with 4 test methods; no semantic complexity distinction (`Where`+`OrderBy`+`Take` ranks the same as nested `SelectMany`+aggregates). Pure team-policy observability rule — appropriately harsh-scored, no investment warranted. |
| LC043 | Prefer await foreach over buffering async streams | Execution & Async | Info | 4 | 4 | 4 | 4 | 3 | 2 | Low | Intentionally narrow immediate-buffer-then-loop detection with proven `IAsyncEnumerable<T>` source gates; 10 test methods cover the basic list/array reports, fixer/fix-all output, cancellation-token buffer arguments, second uses, non-stream lookalikes, and nested lambda/local-function captures that must suppress the fixer. Streaming optimization, not a correctness issue. |
| LC044 | AsNoTracking entity mutated then SaveChanges | Change Tracking & Context Lifetime | Warning | 4 | 4 | 4 | 4 | 5 | 5 | Low | Reference-quality manual rule (chain proof, context-symbol identity, single-assignment gate, block reachability, earlier-save and re-attach gates); honours the last tracking directive (5.5.6) and counts compound assignment / increment as mutations (5.5.10). 81-line doc is a DS=5 anchor. **Tests move 3→4**: nested-scope reachability (mutation inside `if`/`else`/`using`/`while` blocks that fall through to `SaveChanges`) and additional foreach-mutation shapes are now covered (35 tests). Deferred by design: a re-attach in an untaken branch suppresses (treating visible re-attach as intent is a reasonable FP trade-off). |
| LC045 | Missing Include — navigation on materialized entity | Loading & Includes | Warning | 4 | 3 | 4 | 4 | 4 | 5 | Low | **New in 5.6.0; conditional-access surface adversarially probed 2026-06-10.** Detects the canonical EF Core read-side bug: navigation access on a materialized entity without `Include` — N+1 with lazy proxies, silent null without. The dedicated post-crash adversarial pass found **no remaining crash/hang shapes** (deep `?.` chains, `!`/cast mixes, interpolation, nested conditional access in arguments, `?.` invocations all clean) and five FNs — every null-conditional spelling of already-flagged shapes (`First()?.Customer?.Name`, `First()?.Customer.Address?.City`, `First()?.Customer.Clear()`, `orders?[0].Customer`, `var o = orders?[0]`) — all fixed with regression locks. Parenthesized conditional regrouping now reports the full nested path (`(order?.Customer)?.Address` → `Customer.Address`, `(order?.Customer?.Address)?.Region` → `Customer.Address.Region`, inline materializer and inherited-navigation forms included) instead of letting an included parent hide a missing child Include, while method-call results such as `(order?.Customer.GetDetached())?.Address` remain outside the receiver path (70 tests). A stays 4 and FP stays `3`: `AutoInclude()` is invisible and null-guarded access deliberately fires (documented). Remaining watch item: C# 14 null-conditional assignment for newer-Roslyn consumers. |

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
| LC032 | FS 3→4, T 3→4, DS 3→4 | The prior row was stale: the rule now has a full async-aware fixer (20K, declines documented), currently 40 tests (26 fixer — the old note claimed 14 total), and a 117-line doc with a safety contract and unsupported-receiver decline. |
| LC023 | FS 4→3, T 4→3, Priority Low→Medium | The shipped fixer rewrites `FirstOrDefault(pk)` → `Find`, and `Find`'s change-tracker hit bypasses global query filters — on a `HasQueryFilter` entity the rewrite can return an already-tracked soft-deleted / other-tenant row the original filtered query would not have (the DB-query path applies filters, so the hazard is the tracked-instance case). Deferred since Round 2 and still unresolved; no query-filter negatives in the 16-test suite. This was, as of 2026-06-10, the only *then-known* live shipped-fix correctness hazard — **superseded 2026-07-02**: the multi-agent review test-confirmed four more shipped unsafe fixers (LC003, LC008, LC026, LC032) plus two analyzer StackOverflow crashes (LC011, LC037). See the 2026-07-02 section. |
| LC039 | DS 4→3 | The doc is 24 lines — below this document's own calibration bar ("a 30-line doc … will not score above 3"). |
| LC028 | T 3→2 | 8 test methods, no sibling-chain or editorconfig-override coverage; consistent with LC029's T=2 at 5 methods. |
| LC006, LC015, LC018, LC035 | Priority Medium→Low | All named defects from the 2026-05-29/06-04 sweeps shipped; what remains is documented residuals or non-source-detectable gaps. |
| LC008 | Priority Low→Medium | Aligns the column with the planning shortlist (deferred orphaned-warning item is real and scoped). |

Everything else was confirmed at its existing score — including the deliberate harsh marks (LC042 A2/FP2, LC022's EF-9-driven downgrade, LC016 Imp2) and the DS=5 anchors (LC011, LC017, LC030, LC036, LC044, LC007).

## 2026-06-13 LC044 hardening pass

Single-rule precision pass on the Tier-1 silent-data-loss rule.

| Rule | Change | Why |
| --- | --- | --- |
| LC044 | **T 3→4** | Added nested-scope reachability coverage (`if`/`else`/`using`/`while` bodies whose control flow falls through to `SaveChanges`) plus additional foreach-mutation guardrails (queryable `AsNoTracking()` source, nested `if` inside the loop body). The analyzer's block-reachability check now handles ancestor/descendant block relationships and explicit `return`/`throw` terminators instead of requiring the mutation and save to share the same immediate block. Total LC044 tests: 35. |

## 2026-06-23 LC034/LC037 docs hardening pass

Focused security-docs pass on the raw SQL rule boundary.

| Rule | Change | Why |
| --- | --- | --- |
| LC034 | **DS 3→4** | Expanded the rule doc with parameterized `ExecuteSqlRaw` alternatives, quoted-interpolation limits, and concrete examples showing direct `ExecuteSqlRaw` interpolation/concatenation as LC034, direct `FromSqlRaw`/`SqlQueryRaw<T>` interpolation as LC018, and hidden constructed-SQL aliases as LC037. |
| LC037 | **DS 3→4** | Added concrete `string.Format`, `string.Concat`, `StringBuilder`, `SqlQueryRaw<T>`, and parameterized rewrite examples, plus an explicit LC018/LC034/LC037 ownership split so constructed SQL flows are easier to remediate without double-report confusion. |

## 2026-06-23 LC039 docs hardening pass

Focused reliability-docs pass on the repeated-save advisory.

| Rule | Change | Why |
| --- | --- | --- |
| LC039 | **DS 3→4** | Expanded the rule doc with batching guidance, explicit EF Core transaction examples, branch and try/catch/finally boundaries, separate-context and executable-root scoping, EF-only transaction-boundary recognition, and the manual-only rationale for keeping or rewriting repeated saves. |

## 2026-06-23 LC028 test-depth pass

Focused coverage pass on the deep eager-loading review rule.

| Rule | Change | Why |
| --- | --- | --- |
| LC028 | **T 2→3** | Added regression coverage for invalid `dotnet_code_quality.LC028.max_depth` fallback, sibling include-chain depth reset, and per-chain diagnostics when multiple sibling chains exceed the configured threshold. The rule remains a heuristic/manual-review warning, so deeper behavior investment is low priority. |

## 2026-06-23 LC019 docs/test-depth pass

Focused coverage and documentation pass on the conditional Include correctness rule.

| Rule | Change | Why |
| --- | --- | --- |
| LC019 | **T 3→4, DS 3→4** | Added coverage for `ThenInclude` coalesced receiver paths, filtered Include ordering/window conditionals that must stay quiet, and non-EF `ThenInclude` lookalikes. Expanded the doc with the split-query vs. projection vs. eager-load-both decision path, branch-specific filtered Include guidance, and the filtered-Include boundary. |

## 2026-06-23 LC038 docs/test-depth pass

Focused low-importance polish pass on the excessive eager-loading review rule.

| Rule | Change | Why |
| --- | --- | --- |
| LC038 | **T 3→4, DS 3→4** | Added threshold fallback/lowering coverage, remaining transparent EF query-option coverage (`AsTracking`, `AsNoTrackingWithIdentityResolution`, `AsSingleQuery`), and non-EF Include lookalike negatives. Expanded the doc with intentional large-load rationale, projection/separate-query alternatives, split-query limits, and the LC006 boundary. Importance stays 2 because modern EF split queries and explicit projections often make this a review-smell rather than a correctness problem. |

## 2026-06-23 LC035 docs/test-depth pass

Focused coverage and documentation pass on the bulk execute safety rule.

| Rule | Change | Why |
| --- | --- | --- |
| LC035 | **T 3→4** | Added coverage for overwritten earlier unfiltered assignments, conditional reassignment to another filtered local, multiple optional filtered narrowings, and unfiltered catch-path reassignment. Expanded the doc with every-path filtering guidance, project-local `Where` boundaries, and the no-fixer rationale. DS stays 4; the doc was already adequate, but now mirrors the analyzer's local-assignment contract more directly. |

## 2026-06-23 LC026 docs/test-depth pass

Focused coverage and documentation pass on the cancellation-token async-call rule.

| Rule | Change | Why |
| --- | --- | --- |
| LC026 | **T 3→4, DS 3→4** | Added multi-token fixer coverage for `ct` preference when `cancellationToken` is unavailable, readable property tokens, field-token replacement for `CancellationToken.None`, and named-default replacement when multiple tokens exist. Expanded the doc with the local token-selection contract, field/property handling, ambiguity boundaries, and the no-new-token fixer rationale. Analyzer/FP stay 3 because the rule deliberately avoids inferring business intent between domain-specific tokens. |

## 2026-06-23 LC003 docs/test-depth pass

Focused coverage and documentation pass on the Any-over-Count existence-check rule.

| Rule | Change | Why |
| --- | --- | --- |
| LC003 | **T 3→4, DS 2→4** | Added scalar-context coverage for boolean assignments, return expressions, and async `LongCountAsync` replacement with `AnyAsync`. Expanded the doc with provider cost guidance, supported comparison patterns, threshold boundaries where `Count()` is still correct, `IQueryable` scope, and exact fixer behaviour. Analyzer stays 3 because the rule is intentionally limited to direct binary comparisons rather than deeper dataflow. |

## 2026-06-23 LC001 fixer/docs pass

Focused fixer and documentation pass on the local-method client-evaluation rule.

| Rule | Change | Why |
| --- | --- | --- |
| LC001 | **FS 2→3, T 3→4, DS 3→4** | Added analyzer/fixer coverage for static `Queryable` syntax, including fully qualified `System.Linq.Queryable`, aliases to `System.Linq.Queryable`, reordered named `source:` arguments, bare/alias fallback shadowing, ordered static continuations such as `ThenBy`, extension/static ordered source chains, nested static `Queryable` chains, and a semantic guard proving extension receivers named `Queryable` stay on the extension-fixer path. Updated the fixer to rewrite static calls and their static source continuations to `Enumerable` with an explicit `AsEnumerable()` source boundary. Expanded the doc with the client-evaluation trade-off, preferred SQL-translatable rewrites, mapped-function/projectable alternatives, intentional client-side filtering, reported query positions, and safe non-row-dependent helper cases. Analyzer/FP stay 4/3 because provider-specific translation proof and business-intent client evaluation remain intentionally local and conservative. |

## 2026-06-23 LC021 suppression/test-depth pass

Focused suppression-contract coverage pass on the `IgnoreQueryFilters` security rule.

| Rule | Change | Why |
| --- | --- | --- |
| LC021 | **T 3→4** | Added suppression-path coverage for type-level `SuppressMessage`, static extension-call pragma suppression, `.editorconfig` severity suppression, and generated-code exclusion, complementing the existing direct diagnostic, non-EF lookalike, `IEnumerable`, local pragma, and method-level `SuppressMessage` coverage. Updated the doc to distinguish narrow reviewed suppressions from broader project-policy disablement. Analyzer/FP/FS/DS stay unchanged because the rule's EF-only diagnostic and narrow fixer contract were already correct. |

## 2026-06-25 LC004 docs hardening pass

Focused documentation pass on the `IQueryable` to `IEnumerable` API-boundary rule.

| Rule | Change | Why |
| --- | --- | --- |
| LC004 | **DS 3→4** | Expanded the rule doc with the decision path between `IQueryable<T>` signatures and explicit `.ToList()` materialization, forwarding-chain examples, expression-bodied and query-syntax consumption, safe deferred boundaries, source-body limits, nested local-function/lambda scoping, and the narrow `.ToList()` fixer contract. Analyzer/FP/FS/T stay unchanged because the existing implementation and tests already cover these behaviours. |

## 2026-06-25 LC002 docs and sample hardening pass

Focused documentation/sample pass on premature materialization boundaries.

| Rule | Change | Why |
| --- | --- | --- |
| LC002 | **DS 3→4** | Expanded the rule doc with `ToList()`/`ToArray()`/`AsEnumerable()` boundary semantics, reported continuation families, provider-safe lambda examples, intentional client-boundary guidance, non-goals for locals/properties/constructors/control-flow assignments, and fixer behaviour for sequence, terminal, redundant, and shape-changing cases. Updated the executable sample so its labelled violations correspond to real LC002 diagnostics. Analyzer/FP/FS/T stay unchanged because this is a documentation and sample clarity pass over existing behaviour. |

## 2026-06-25 LC008 docs and sample hardening pass

Focused documentation/sample pass on sync-over-async boundaries.

| Rule | Change | Why |
| --- | --- | --- |
| LC008 | **DS 3→4** | Expanded the rule doc with the mapped EF async counterpart families, guidance for APIs with no async equivalent, query-expression translation boundaries, async-context scoping, static-local-function handling, fixer limits for non-async lambdas/local functions, cancellation-token non-goals, and scalar terminal examples. Updated the sample to include a scalar terminal diagnostic in addition to materialization. Analyzer/FP/FS/T stay unchanged because existing implementation and tests already cover these behaviours. |

## 2026-06-26 LC018 SqlQueryRaw fixer pass

Focused fixer pass on the raw-SQL query API family.

| Rule | Change | Why |
| --- | --- | --- |
| LC018 | No score change | Added a safe `SqlQueryRaw<T>` → `SqlQuery<T>` fixer for direct interpolated scalar/keyless query SQL, preserving generic type arguments and keeping the existing guards for quoted interpolation holes, concatenation, and raw-parameter calls. Added focused fixer coverage and updated docs/README/sample guidance for the `FromSqlRaw`/`SqlQueryRaw<T>` safe-sibling split. FS stays `4` because the remaining manual shapes are deliberate safety boundaries rather than missing direct rewrites. |

## 2026-06-26 LC045 parenthesized conditional path pass

Focused precision pass on the Tier-1 missing-Include rule's null-conditional path reporting.

| Rule | Change | Why |
| --- | --- | --- |
| LC045 | No score change | Closed the parenthesized conditional regrouping residual: `(order?.Customer)?.Address?.City` now reports `Customer.Address`, deeper regrouping such as `(order?.Customer?.Address)?.Region` reports `Customer.Address.Region`, inline materializer forms such as `(db.Orders.Include(o => o.Customer).FirstOrDefault()?.Customer)?.Address` are covered, and inherited navigation segments are resolved through base entity types. Added a guard so conditional method-call results such as `(order?.Customer.GetDetached())?.Address` do not get appended to the queried receiver path, plus regression tests and docs/README guidance. Scores stay unchanged because the analyzer already had broad conditional-access coverage; this removes a named polish gap without changing the conservative FP boundaries. |

## 2026-06-26 LC005 local sort reset pass

Focused precision pass on the multiple-`OrderBy` rule's direct local-hop residual.

| Rule | Change | Why |
| --- | --- | --- |
| LC005 | **DS 3→4** | LC005 now follows a single-assignment local whose initializer is already sorted, so `var sorted = q.OrderBy(...); sorted.OrderBy(...)` reports the reset, including parenthesized initializers. The existing `ThenBy` fixer is offered only when the receiver still has an ordered type, including static `Enumerable`/`Queryable` syntax; locals widened to `IEnumerable<T>`/`IQueryable<T>` report as manual fixes, and reassigned locals, deconstruction writes, plus `out`/`ref` writes stay quiet to avoid path-sensitive false positives. Docs/README now explain the local-hop support, query-syntax report-only behavior, and the fields/properties/helper-method boundary. Analyzer/FP/FS/T stay unchanged because this is a narrow straight-line precision improvement with 22 total analyzer/fixer tests. |

## 2026-06-26 LC027 relationship-builder local pass

Focused false-positive pass on the explicit foreign-key modeling rule.

| Rule | Change | Why |
| --- | --- | --- |
| LC027 | No score change | LC027 now follows a single-assignment relationship-builder local when `HasForeignKey(...)` is called separately from the `HasOne(...).WithMany(...)` or `HasOne(...).WithOne(...)` chain, so intentionally configured shadow FKs no longer receive a missing-FK diagnostic. Reassigned builder locals stay conservative because the configured navigation is ambiguous, including normal assignment, deconstruction, and `ref` writes, while reused local names in separate scopes, nested lambda shadow locals, shadowed non-builder locals, shadowed parameters, out-var/foreach shadows, and same-name member assignments resolve independently. Docs/README now describe the split-chain support and the reassignment boundary. Tests move from 14 to 27, but the score stays conservative because inherited configuration patterns remain lightly covered. |

## 2026-06-26 LC043 nested-capture fixer guard pass

Focused unsafe-fix pass on async-stream buffering.

| Rule | Change | Why |
| --- | --- | --- |
| LC043 | **T 3→4** | The analyzer now counts buffered-local references inside nested lambdas and local functions as additional uses, so it no longer offers the `await foreach` fixer when removing the buffer would leave captured code broken. Added regression coverage for both nested capture shapes while preserving the existing immediate list/array fixes, fix-all, cancellation-token buffer guard, second-use guard, and non-stream lookalike negative. Analyzer/FP/FS/DS stay unchanged because the rule remains deliberately narrow and the docs are adequate but compact. |

## 2026-06-26 LC022 modern EF guidance pass

Focused sample and documentation truth pass on a marginal advisory rule.

| Rule | Change | Why |
| --- | --- | --- |
| LC022 | **DS 3→4** | Updated the executable sample and README to match the current rule contract: modern EF Core can translate some correlated collection projections, so LC022 is an advisory query-shape review rather than a blanket translation failure. The sample now points users toward direct projection, split queries, or keeping the materializer when a DTO contract needs a concrete collection, and README documents the narrow `ToList()`-only fixer boundary. Analyzer/FP/FS/T stay unchanged because this pass does not alter behaviour. |

## 2026-06-26 LC041 null-conditional scalar read pass

Focused false-negative pass on the single-entity scalar projection advisory.

| Rule | Change | Why |
| --- | --- | --- |
| LC041 | **T 2→3, DS 2→3** | Closed the null-conditional single-property read residual: `var user = users.FirstOrDefault(...); Console.WriteLine(user?.Name);`, `user?.Name.Length`, and `user?.Name.Trim()` now report the same over-fetch as direct `user.Name` chains, while null-conditional reads remain diagnostic-only so the fixer does not remove the entity and leave stale `?.` member access behind. `FirstOrDefault`/`SingleOrDefault` forms also still receive no fixer because projecting before optional materializers changes null/default semantics. Updated tests, docs, and the executable sample. Analyzer/FP/FS/Importance stay unchanged because the remaining hoisted-predicate residual is still deliberately unresolved and the rule remains a low-impact advisory. |

## 2026-06-26 LC029 identity Select boundary pass

Focused false-negative and guidance pass on the redundant identity projection advisory.

| Rule | Change | Why |
| --- | --- | --- |
| LC029 | **T 2→3** | Added coverage for statement-bodied interface-enumerable identity projections (`items.Select(x => { return x; })`), parenthesized/cast/null-forgiving fluent receivers, type-changing projections (`items.Select(x => (object)x)`) that must stay quiet, awaited-task projections (`items.Select(async x => await x)`) that must stay quiet, explicit-cast projections (`items.Select<Base, Base>(x => (Derived)x)`) that must stay quiet, concrete enumerable receivers such as `List<T>` that must stay quiet, and static `Enumerable.Select(...)` forms that the fluent fixer cannot safely rewrite. Also locked the fixer contract that explicit boundaries such as `AsEnumerable()` are preserved when the redundant `Select(x => x)` is removed. The docs, README, and executable sample now tell users to keep real client/materialization boundaries directly instead of using identity projection as a marker. Analyzer/FP/FS/DS/Importance stay unchanged because this remains a cosmetic, low-impact rule and the shipped fixer shape was already conservative. |

## 2026-06-26 LC031 unbounded materialization guidance pass

Focused test-depth and remediation-guidance pass on the unbounded materialization advisory.

| Rule | Change | Why |
| --- | --- | --- |
| LC031 | **FS 3→4, T 3→4, DS 3→4** | Added coverage for query syntax over `DbContext.Set<TEntity>()`, bounded query-syntax aliases, `Skip(...)` without `Take(...)`, `TakeLast(...)`, and transparent query options such as `AsNoTracking()`. Expanded docs, README, and executable sample with the manual-only fixer rationale, intentional full-scan guidance, export/streaming/batching alternatives, and the non-bounding operator list (`Where`, `OrderBy`, `Skip` alone, `TakeLast`, `Chunk`, and query options). Analyzer/FP/Importance stay unchanged because this pass validates and documents the existing conservative chain walker rather than changing diagnostics. |

## 2026-06-26 LC040 mixed tracking guidance pass

Focused test-depth and remediation-guidance pass on the mixed tracking/no-tracking advisory.

| Rule | Change | Why |
| --- | --- | --- |
| LC040 | **T 3→4, DS 3→4** | Added coverage for transparent EF query options (`AsSplitQuery()`/`TagWith(...)`) and explicit transactions to prove they do not hide mixed tracking-mode evidence. Expanded docs, README, and executable sample with legitimate split-workflow guidance, separate scope/context alternatives, the transaction boundary, and the manual-only fixer rationale. Analyzer/FP/FS/Importance stay unchanged because the implementation already behaved conservatively and this pass locks the intended boundary. |

## 2026-06-26 LC016 clock-boundary guidance pass

Focused documentation and sample truth pass on the clock-in-query advisory.

| Rule | Change | Why |
| --- | --- | --- |
| LC016 | **DS 2→4** | Expanded the rule doc, README guidance, and executable sample to cover deterministic application-clock boundaries, injected-clock/testability guidance, `UtcNow` timestamp preference, provider-specific database-server clock alternatives, modern EF provider variance, and the narrow local-extraction fixer contract. Analyzer/FP/FS/T/Importance stay unchanged because the existing implementation already covers the intended query-expression shapes and this remains low-impact cacheability/testability guidance rather than a correctness rule. |

## 2026-07-02 Multi-agent adversarial review

A fresh, deliberately harsh review ran **45 parallel per-rule reviewers** (one per LC0xx, each reading the analyzer/fixer source, tests, doc, and the rule's own health-doc history) hunting for genuinely new FP/FN/unsafe-fix/crash evidence and metadata drift. Each concrete finding was piped to a **skeptic verifier** told to default to *refuted*, and the surviving high-severity findings were then **test-confirmed in isolated git worktrees** by writing a real red test and running it on `net10.0` (the local harness runs cleanly single-target; the historical CS0518 note is multi-target only).

### Test-confirmed defects (net10.0)

Seven defects reproduced against the built analyzer. Four are shipped unsafe fixers, two are analyzer StackOverflow crashes, one is a high-value false negative. Scorecard rows and the Planning Shortlist are updated above; this corrects the prior standing claim that LC023 was "the only live shipped-fix correctness hazard".

| Rule | Class | Confirmed behaviour | Deciding code | Score change |
| --- | --- | --- | --- | --- |
| LC037 | **crash** (High; fixed below) | `sql = sql + id;` before `ExecuteSqlRaw(sql)` → unbounded recursion → **StackOverflow aborts the analysis host** on valid compiling code (no visited/depth guard; write-position guard uses the LHS span). | `RawSqlStringConstructionLocalResolution.cs:44` | A 4→3, Pri→High, restored after fix |
| LC003 | **unsafe-fix** (High; fixed below) | `Count() == Empty` (`const int Empty=0`) → `Any()` instead of `!Any()` — **silent logical inversion** (fixer negates only on a bare `0` literal token). | `AnyOverCountFixer.cs:148` | FS 4→2, Pri→High, restored after fix |
| LC008 | **unsafe-fix** (High; fixed below) | `db.Users.ToList().Count` → `await db.Users.ToListAsync().Count` (no parens) → binds as `await (….Count)` → **CS1061 build break**. | `SyncBlockerFixer.cs:96-126` | FS 3→2, Pri→High, restored after fix |
| LC026 | **unsafe-fix** (High; fixed below) | `query.Where(...).ToListAsync()` → token appended to the innermost `Where(...)` not the diagnosed `ToListAsync()` → **build break**, diagnostic persists. | `MissingCancellationTokenFixer.cs:39` | FS 4→2, Pri→High, restored after fix |
| LC032 | **unsafe-fix** (High; fixed below) | fixer transplants a `Skip`/`Take`/`Distinct` source chain verbatim into the `ExecuteUpdate` receiver → EF **throws `InvalidOperationException` at runtime** (working code → runtime exception). | `ExecuteUpdateForBulkUpdatesQueryAnalysis.cs:19` | FS 4→2, Pri→High, restored after fix |
| LC014 | **FN** (High; fixed below) | EF async predicate terminals (`AnyAsync`/`CountAsync`/`FirstOrDefaultAsync`/…) on `EntityFrameworkQueryableExtensions` never fire; the sync equivalents do — sync/async asymmetry on the dominant EF style. | `AvoidStringCaseConversionAnalyzer.cs:122-127` | A 4→3, Pri→Medium, restored after fix |
| LC011 | **crash** (low; fixed below) | self-referential builder local in `OnModelCreating` (`var entity = entity.HasKey("Id");`) → infinite `TryResolveLocalBuilder`↔`TryResolveEntityTypeFromBuilderExpression` recursion → **StackOverflow** (IDE-live on non-compiling source only). | `EntityMissingPrimaryKeyConfigurationScan.cs` | Pri→Medium, restored after fix |

### Metadata-drift corrections

- **Doc-category mismatches (corrected).** `docs/LC005_MultipleOrderBy.md` now states `Category: Performance`, matching the shipped `DiagnosticDescriptor`; `docs/LC036_DbContextCapturedAcrossThreads.md` now states `Category: Safety`.
- **Test-count drift.** LC017 = **37** methods (row said 38); LC023 = **24** (row said 26, and the 2026-06-10 rerun table says 16 — both are wrong); LC025 = **29** (row said 31); LC044 = **35** (row and 2026-06-13 pass said 30 — the suite *grew*, an undercount). LC003/LC005/LC030/LC032/LC035/LC036 test counts verified correct; LC011 now has 47 tests after the recursion-guard regression.
- **Doc line-count drift is uniform +5.** Every per-rule doc line count cited in the scorecard is stale by exactly five lines (e.g. LC006 94→99, LC007 82→87, LC015 112→117, LC017 171→176, LC030 81→86, LC036 41→46, LC044 76→81) — a systematic shift from a shared trailer added in the 2026-06-26 metadata pass. Benign (boilerplate, not substance), so the DS anchors hold; the LC011, LC017, LC032, and LC044 numbers are corrected inline above where their rows were already being edited.

### 2026-07-02 LC037 recursion guard pass

| Rule | Change | Why |
| --- | --- | --- |
| LC037 | **A 3→4, Priority High→Low** | Added a regression for the valid self-referential assignment `sql = sql + id; ExecuteSqlRaw(sql)`, which previously aborted the test host with StackOverflow. The local resolver now excludes writes whose full operation has not completed before the reference being resolved and caps recursive local/StringBuilder chasing, so the analyzer reports LC037 normally while existing alias, overwrite, and conditional-write tests stay green. |

### 2026-07-02 LC003 constant-zero fixer pass

| Rule | Change | Why |
| --- | --- | --- |
| LC003 | **FS 2→4, Priority High→Low** | Added a fixer regression for `Count() == Empty` with `const int Empty = 0`, which previously rewrote to `Any()` and inverted the empty-check. The fixer now uses Roslyn constant values for the zero side of equality comparisons, so named zero constants and bare zero literals both rewrite to `!Any()` while the existing `Any()` and `AnyAsync()` cases stay green. |

### 2026-07-02 LC008 awaited-receiver fixer pass

| Rule | Change | Why |
| --- | --- | --- |
| LC008 | **FS 2→4, Priority High→Low** | Added fixer regressions for `db.Users.ToList().Count` and `db.Users.FirstOrDefault()?.Name`, which previously rewrote to continuations bound to the returned `Task`. The fixer now wraps awaited async calls in parentheses when the original sync invocation is the receiver for `.` / `[]` / `()` / `?.`, preserving direct `await db.Users.ToListAsync()` rewrites and keeping query-expression and non-async-context guardrails intact. |

### 2026-07-02 LC026 diagnosed-invocation fixer pass

| Rule | Change | Why |
| --- | --- | --- |
| LC026 | **FS 2→4, Priority High→Low** | Added a fixer regression for `query.Where(...).ToListAsync()`, which previously appended the token to the inner `Where(...)` and left the EF async terminal unfixed. The code fix now selects the invocation node covered by the diagnostic span, so chained query receivers update the diagnosed async call while existing default-token replacement and token-selection behaviour stays green. |

### 2026-07-02 LC032 unsupported-receiver fixer pass

| Rule | Change | Why |
| --- | --- | --- |
| LC032 | **FS 2→4, Priority High→Low** | Added fixer regressions for `foreach` sources containing fluent and static `Queryable.Take(...)`, which previously offered an `ExecuteUpdate` rewrite that EF would reject at runtime. The fixer now declines receiver chains containing `Skip`, `Take`, or `Distinct`, leaving the diagnostic as manual guidance while preserving safe direct, filtered, ordered, materialized, async, cancellation-token, and duplicate-assignment fixer cases. |

### 2026-07-02 LC014 EF async-terminal pass

| Rule | Change | Why |
| --- | --- | --- |
| LC014 | **A 3→4, Priority Medium→Low** | Added an analyzer regression for `AnyAsync(u => u.Name.ToLower() == value)`, which previously stayed silent because LC014 only recognised `System.Linq.Queryable` methods. The analyzer now recognises EF Core's async predicate terminals on `EntityFrameworkQueryableExtensions`, so async `AnyAsync`/`CountAsync`/`FirstOrDefaultAsync`-style predicates get the same diagnostics as their synchronous counterparts while keeping LINQ-to-Objects and non-EF sources quiet. |

### 2026-07-02 LC011 builder-local recursion guard pass

| Rule | Change | Why |
| --- | --- | --- |
| LC011 | **Priority Medium→Low** | Added a malformed-source regression for `var entity = entity.HasKey("Id");`, which previously aborted the test host with StackOverflow while chasing the self-referential local builder. Builder-expression resolution now tracks visited expressions per lookup and bails out on cycles, preserving valid `modelBuilder.Entity<T>()` locals and chained-builder `HasKey` detection while keeping IDE-live analysis alive during incomplete edits. |

### 2026-07-03 LC037 StringBuilder statement-flow pass

| Rule | Change | Why |
| --- | --- | --- |
| LC037 | No score change | Test-confirmed and closed the 2026-07-02 candidate that a `StringBuilder` built through separate `Append(...)` statements was missed when `builder.ToString()` reached `ExecuteSqlRaw(...)`. LC037 now inspects prior append statements on the same builder local and reports non-constant appended values, including null-conditional appends, local dynamic append values, loop-carried and compound-assigned append-local writes, caught-throw continuations with exact, alias, ordinary base, user-defined base, and framework base exception catches, fluent `Clear().Append(...)` chains, builder-local aliases, self-preserving assignments, conditional builder value writes, conditional alias writes, copied builder expressions, copy assignments from tainted builders, constructor copies from tainted builders, short-circuit clears, and short-circuit assignment resets, while constant-only append statements, branch-selected literal append locals, path-dominated constant append-local overwrites, per-iteration constant append-local resets, variable-capacity constructors, constant compound assignments, terminating-branch local writes, loop-guarded branch clears, same-loop branch exits, try/catch-contained branch clears, catch-exiting throws, first matching catch clauses that exit, constant-false catch filters, `ApplicationException` handlers for non-derived throws, custom exception names that only suffix-match a framework catch type, guaranteed `finally` clears, conditional `finally` clears, early-return/loop/switch/nested terminating guard paths, only-surviving-branch clears, and guaranteed fluent or direct `Clear()` resets stay quiet. The alias matcher tracks the guaranteed write that produced the current builder instance, so reassigning the original builder before a later append or clear does not taint or sanitize an older alias, maybe-reassigned alias clears do not sanitize a builder unless the alias is definite, and reset assignments that read the old builder do not discard taint. Security importance was already 5 and the rule was already Low after the larger raw-SQL hardening batch, so scores stay unchanged; LC037 now has 83 tests. |

### Candidate queue — source-reasoned, NOT yet test-confirmed

The review also surfaced the findings below by reasoning from source. They passed the skeptic pass but were **not** individually reproduced against the built analyzer, so they are leads for the next hardening pass, **not** verified defects — construct a red test and confirm each before promoting or rescoring. (This is exactly how several 2026-06-04 probe claims were later *rejected* on verification.)

| Rule | Class | Lead (unverified) |
| --- | --- | --- |
| LC001 | FN | LC001 misses a local/user method call inside a NESTED lambda when that method references only the OUTER (queryable-bound) range variable and not the inner… |
| LC001 | unsafe-fix | When LC001 fires for a local method inside a nested correlated subquery (the existing TestCrime_NestedLambda_LocalMethodInInnerLambda shape), the fixer re… |
| LC002 | unsafe-fix | LC002's 'Move query operator before materialization' code fix is offered for the terminal continuations Last/LastOrDefault and rewrites the materialized i… |
| LC006 | FN | LC006 groups sibling collections by the *literal full name path* of their parent (parentKey = join of ALL preceding segment names, references included). A… |
| LC007 | FN | A deferred `AsEnumerable()` immediately before a terminal aggregate escapes detection. When the terminal execution method (e.g. Count/Any/Sum/First) has `… |
| LC007 | FN | A materialized EF query used as the SOURCE of an inner foreach that is itself nested inside another loop is never attributed to the outer loop, so it is n… |
| LC009 | FP | MaterializedEntityIsMutated only recognizes a mutation when the assignment/increment target is a property whose immediate Instance is a LocalReference to… |
| LC010 | FP | The analyzer flags any SaveChanges/SaveChangesAsync whose enclosing loop shares its executable root, with no awareness of whether the DbContext is a fresh… |
| LC010 | unsafe-fix | The 'Move SaveChanges after loop' fixer performs no scope/data-flow analysis on the statement it relocates. TryGetMovableSaveStatement only checks loop ki… |
| LC011 | FP | ApplyConfigurationsFromAssembly is only honoured when the argument is literally `typeof(X).Assembly` with X in the current compilation. The mainstream for… |
| LC012 | unsafe-fix | The fixer blindly appends .ExecuteDelete()/.ExecuteDeleteAsync() to whatever IQueryable is passed as the RemoveRange argument (Arguments[0].Expression), w… |
| LC013 | FP | TryGetQueryChainReceiver treats EVERY extension method whose result is deferred-typed as a transparent query-chain link, recursing to its first argument a… |
| LC015 | FN | HasSortingDownstream suppresses the Skip/Take/Last/ElementAt diagnostic whenever ANY downstream invocation is named OrderBy/OrderByDescending/ThenBy/ThenB… |
| LC016 | unsafe-fix | In an expression-bodied member returning IQueryable, the analyzer fires but the fixer is a silent no-op: memberAccess.AncestorsAndSelf().OfType<StatementS… |
| LC017 | unsafe-fix | The LC017 fixer under-detects accessed properties relative to the analyzer, so applying the fix can produce non-compiling code. The analyzer's usage analy… |
| LC018 | unsafe-fix | The LC018 fixer rewrites FromSqlRaw($"...")->FromSqlInterpolated($"...") (and SqlQueryRaw<T>->SqlQuery<T>) whenever the sql argument is an interpolated st… |
| LC021 | unsafe-fix | The LC021 fixer picks its replacement node with `Arguments.Count > 0 ? ArgumentList.Arguments[0].Expression : memberAccess.Expression`, using "has argumen… |
| LC023 | unsafe-fix | LC023 fires and its fixer breaks the build for a self-referential key predicate where the non-key side references the same lambda parameter (a column-to-c… |
| LC024 | FN | AnalyzeInvocation bails immediately when method.Name != "Select", so the GroupBy result-selector overload GroupBy(keySelector, (key, group) => ...) is nev… |
| LC025 | FN | LC025 does not recognize AsNoTrackingWithIdentityResolution() as a no-tracking marker. IsEfCoreAsNoTracking gates strictly on method name == "AsNoTracking… |
| LC025 | unsafe-fix | On a multi-origin shape where the analyzer legitimately fires (every origin untracked), the fixer's FindAsNoTrackingOrigin picks only the single latest or… |
| LC027 | unsafe-fix | When the principal entity's primary key is non-conventional (configured via fluent HasKey on a property that is not named 'Id'/'{Type}Id' and lacks [Key])… |
| LC030 | FN | AnalyzeField and AnalyzeProperty early-return for static members, so a static DbContext field/property is never a candidate. A static readonly DbContext i… |
| LC030 | FP | AnalyzeProperty flags ANY property whose type derives from DbContext on a proven long-lived type, with no distinction between an auto-property (which actu… |
| LC031 | FN | IsCollectionMaterializer only matches ToList/ToArray/ToDictionary/ToHashSet (+async). ToLookup — a System.Linq operator that forces full client-side mater… |
| LC035 | FP | HasWhereInLocalInitializer requires a *latest unconditional* filtered base assignment; when a local is declared without an initializer and definitely assi… |
| LC035 | FP | HasWhereInChain never descends into IConditionalOperation (ternary) or ISwitchExpressionOperation branches. When the ExecuteDelete/ExecuteUpdate receiver… |
| LC036 | FN | IsTargetThreadApi only matches Parallel.ForEach (method.Name == "ForEach"). Two other first-class TPL parallel APIs in the SAME System.Threading.Tasks.Par… |
| LC036 | FN | TryFindCapturedDbContext locates a lambda only via syntax.AncestorsAndSelf(). When the constructor/callback argument explicitly wraps the lambda in a dele… |
| LC039 | FP | AreMutuallyExclusiveBranches only recognizes if-statements, switch-statements, and try/catch as mutually-exclusive constructs. It does NOT recognize switc… |
| LC040 | FN | LC040 misses mixed tracking when the tracked side is expressed through DbContext.Set<T>() and the context is a method parameter. MixedTrackingAndNoTrackin… |
| LC041 | FN | The LC041 fixer silently offers no fix whenever any query operator sits between the query source and the terminal materializer (e.g. `users.Where(x => x.I… |
| LC041 | unsafe-fix | The LC041 fixer emits non-compiling code when the terminal materializer's predicate is a hoisted Expression/Func variable instead of an inline lambda. Fin… |
| LC044 | FP | The re-attach suppression gate for the local (non-foreach) path only considers Update/Attach/UpdateRange/AttachRange/Entry.State calls that occur STRICTLY… |
| LC045 | unsafe-fix | The LC045 code-fix wraps the query source with `.Include(x => x.Nav)` without ever verifying that the source expression is an `IQueryable<T>`. EF Core's `… |


## Planning Shortlist

Work flows to rules that are high in the Importance Ranking **and** carry health gaps.

| Priority | Rules | Work |
| --- | --- | --- |
| High — **2026-07-02 test-confirmed** | None | LC037's `sql = sql + id` StackOverflow, LC003's named-zero equality inversion, LC008's awaited-receiver build break, LC026's chained-query token insertion build break, and LC032's unsupported `Skip`/`Take`/`Distinct` receiver fix have all been fixed and regression-locked. |
| Medium — **2026-07-02 test-confirmed** | None | LC011's self-referential builder-local StackOverflow and LC014's EF async predicate-terminal asymmetry have been fixed and regression-locked. |
| Low — opportunistic, Tier-1-importance hygiene first | None | LC045 parenthesized conditional path reporting, LC018 `SqlQueryRaw<T>` fixer residual, LC005 local sort reset, LC027 relationship-builder local false positive, LC043 nested-capture fixer guard, LC022 modern EF guidance, LC041 null-conditional scalar-read FN, LC029 identity-boundary guidance, LC031 docs/test depth, LC040 docs/test depth, LC016 docs/sample depth, LC008 docs/sample depth, LC002 docs/sample depth, LC004 docs depth, LC039 doc expansion, LC028 test depth, LC019 docs/test depth, LC038 docs/test depth, LC035 docs/test depth, LC026 docs/test depth, LC003 docs/test depth, LC021 suppression/test depth, and LC037 statement-based `StringBuilder.Append(...)` flow are addressed. **2026-07-02 candidate queue**: remaining source-reasoned FP/FN/unsafe-fix findings surfaced by the multi-agent review are tabulated in the 2026-07-02 section but are **not yet independently test-confirmed** — treat them as leads for the next hardening pass, not verified defects, and test-confirm each before promoting. Two doc-category mismatches (LC005, LC036) and per-rule test/line-count drift are also corrected there. |

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

Package version: **5.6.34**

Base audited commit: master at `9cf53b6` (5.6.34 release state). Since the 2026-06-04 baseline (5.5.13): descriptor hygiene (helpLinkUri on all rules, sealed/FixAll architecture tests), repo/CI hardening, the `IncludePathParser` extraction shared by LC006/LC045, **LC045 shipped in 5.6.0** (four pre-ship review-hardening rounds), the **5.6.1 hot-fix** for the LC045 chained-`?.` StackOverflowException that killed csc on 5.6.0, and the July 2026 raw-SQL/fixer hardening through 5.6.34.

Architecture tests enforce the rule quality contract for public package metadata, code-fix provider exports, documentation drift, repository layout, and `samples/LinqContraband.Sample/sample-diagnostics.json` sample expectations.

Current verification (2026-07-03, LC037 StringBuilder statement-flow fix PR):

- Focused red/green regression confirmed `ExecuteSqlRaw(builder.ToString())` missed statement-based `StringBuilder.Append(id)` construction before the fix (expected 1 diagnostic, actual 0), then passed with constant-only and `Clear()` guardrails.
- The LC037 net10.0 slice passes 83 tests, including review-driven guards that custom exception names only suffix-matching a framework catch type, non-derived `ApplicationException` catches, returning first catches, and constant-false catch filters do not make an unreachable raw-SQL sink report. Full PR verification passed locally through `RuleCatalogDocGenerator --check`, `dotnet build --no-restore`, `SampleDiagnosticsVerifier --configuration Release --frameworks net8.0 net9.0 net10.0`, `dotnet test tests/LinqContraband.Tests/LinqContraband.Tests.csproj --framework net10.0 --no-build --verbosity normal` (1224 tests), `git diff --check`, hygiene scans, and independent Codex review fallback.
- Local broad multi-target analyzer-verifier tests remain limited on this Mac by the same pre-existing Roslyn test reference issue reproduced on clean `origin/master` (`CS0518`/missing `System.Object`, `DateTime`, and `IQueryable<>` inside verifier compilations). Local arm64 net8.0/net9.0 testhost runs are also unavailable because only arm64 `Microsoft.NETCore.App 10.0.9` is installed.
- Full analyzer test coverage for the release remains delegated to GitHub CI's Ubuntu `dotnet test --no-build --verbosity normal` matrix after PR creation, matching the repository workflow.

Historical baselines: 2026-06-04 rerun verified 919 tests at 5.5.13; 2026-05-29 deep rescan verified 828 tests at 5.4.12 (840d00b); the 2026-05-14 fine-comb re-audit (six parallel slices, scores moved on 30 of 44 rules) established the harsh calibration and the DS=5 anchors (LC011 FP/T/DS, LC030 DS, LC036 DS/Imp) that remain the reference for what a `5` requires.
