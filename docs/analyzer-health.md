# Analyzer Health

Reviewed: 2026-07-16 (LC044 nested-member mutation detection; prior same-day LC034 structural SQL fixer guard; prior 2026-07-15 LC045 direct model-level `AutoInclude()` precision; prior 2026-07-14 LC045 exact query-consumer surface, active-generation callback provenance, and exact materializer symbols; prior 2026-07-09 LC045 origin-aware control flow; prior 2026-07-08 LC010 local-delegate/method-group loop invocation detection, compound and self-combining delegate subscriptions, loop-carried, wrapper-loop-carried, callback-loop-carried, and opposite-branch delegate assignments, delegate-removal and negated-guard stability, nested setup-helper calls, local invoker callback helpers, conditional-branch exclusivity, switch-local retry-break targeting, called-helper overwrite stability, and fresh wrapper-parameter pass-through with parameter-reassignment guards; prior 2026-07-07 LC045 `DbContext.Set<TEntity>()` root detection; prior 2026-07-05 LC009 nested-member mutation guard, LC017 conversion-access no-fix guard, LC041 chained-materializer fixer and hoisted-predicate no-fix guard, LC030 computed DbContext property guard, LC011 current-assembly configuration scan, LC039 switch-expression branch guard, LC013 custom extension boundary, LC030 static DbContext storage guard, LC025 multi-origin fixer guard, LC010 fresh-context-per-iteration guard, LC018 SQL identifier-position fixer guard, LC025 identity-resolution no-tracking guard, LC024 GroupBy result-selector pass, LC031 `ToLookup()` materializer detection, LC040 `DbContext.Set<TEntity>()` tracked-source detection, LC007 nested-foreach source execution detection, and LC006 reference-prefixed sibling detection folded into the 2026-07-04 45-rule current-state audit and candidate queue; prior same-day LC002 `Last*` fixer guard, LC021 named-filter fixer guard, LC012 later-save fixer parity, LC023 self-referential predicate fixer guard, LC027 Fluent key-type fixer guard, LC017 projection-member fixer guard, LC035 filtered-path guard, LC016 expression-bodied fixer guard, LC015 downstream-sort pagination guard, LC045 queryable-source fixer guard, LC044 prior-reattach guard, LC007 deferred `AsEnumerable()` execution guard, LC036 Parallel/delegate callback capture guard, and LC010 scoped-receiver fixer guard; prior: 2026-07-03 LC037 StringBuilder statement-flow fix; 2026-07-02 multi-agent adversarial review with **7 defects test-confirmed**; repo hygiene pass 2026-06-26, full per-rule re-verification 2026-06-10, overnight rerun 2026-06-04, deep rescan 2026-05-29)

This is a deliberately harsh health audit for the **45 analyzers** in `RuleCatalog`. The catalog currently declares 30 rules with code fixes and 15 manual-only rules with explicit rationale. Scores are 1-5, where `5` means reference-quality and hard to improve, `3` means usable but meaningfully incomplete, and `1` means unreliable or underbuilt.

Release metadata:

- Package version: 5.6.46
- Base audited commit: c63e8d67d66e9ea11ba57aefa3c47b483077036d
- Pack verification: `dotnet pack src/LinqContraband/LinqContraband.csproj -c Release -o /tmp/linqcontraband-5.6.46`

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

> Rows below are the **2026-07-04 current-state audit** — the 2026-06-04 rerun deltas, 2026-06-10 full re-verification, LC045 releases, 2026-07-02 adversarial fixes, 2026-07-03 LC037 hardening, and the 2026-07-04/05 LC002 `Last*`, LC021 named-filter, LC012 later-save/query-source, LC023 self-referential predicate, LC027 Fluent key-type, LC017 projection-member, LC035 filtered-path, LC016 expression-bodied, LC015 downstream-sort pagination, LC045 queryable-source, LC044 prior-reattach, LC007 deferred `AsEnumerable()` execution and nested-foreach source execution, LC036 Parallel/delegate callback capture, LC010 scoped-receiver fixer, fresh-context-per-iteration guard, and local-delegate/method-group loop invocation detection, LC025 identity-resolution no-tracking and multi-origin fixer safety, LC024 GroupBy result-selector, LC031 `ToLookup()` materializer, LC040 `DbContext.Set<TEntity>()` tracked-source, LC006 reference-prefixed sibling, LC018 SQL identifier-position fixer guards, LC001 nested-lambda/fixer-safety guards, LC009 nested-member mutation guard, LC017 conversion-access no-fix guard, LC041 chained-materializer fixer and hoisted-predicate no-fix guard, LC030 static DbContext storage detection and computed-property guard, LC013 custom extension boundaries, LC039 switch-expression branch guards, LC011 current-assembly configuration scanning, and LC044 nested-member mutation detection are folded into the rows. Every rule was independently re-checked against current source, tests, docs, samples, and the candidate queue; subsequent hardening has raised the local net10.0 suite to **2,126 tests**.

| Rule | Title | Domain | Severity | Analyzer | False Positives | Fix Strategy | Tests | Docs/Samples | Importance | Priority | Notes |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| LC001 | Local method usage in IQueryable | Query Shape & Translation | Warning | 4 | 3 | 3 | 4 | 4 | 3 | Low | Detection is sound and includes aggregate selectors (`Sum`/`Average`/`Min`/`Max` selector FN fixed in 5.5.11) plus nested query lambdas where the helper depends only on an outer query range variable. Fixer now handles extension syntax and static `Queryable` syntax, including aliases, reordered named `source:`/`outer:` arguments, ordered continuations, extension/static ordered source chains, and nested static continuations, by making the client-evaluation boundary explicit with `AsEnumerable()`/`Enumerable`, with semantic guards for receivers named `Queryable`, static wrappers such as `Queryable.AsQueryable`, upstream static operators that should remain server-side, complex source expressions that need parentheses before `.AsEnumerable()`, and nested correlated query invocations that now stay diagnostic-only instead of receiving a misleading partial inner-boundary rewrite. LC001 has 19 fixer tests and 42 focused tests overall. Docs explain parameterized constants, SQL-translatable alternatives, mapped functions/projectables, the client-eval trade-off, and the nested-correlated no-fix boundary. Modern EF partial client evaluation keeps urgency moderate. |
| LC002 | Premature query continuation after materialization | Materialization & Projection | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | Strong analyzer/fixer boundary after the 2026-07-04 guard pass. LC002 has 47 tests, including 16 fixer tests, and the set/keyed/grouped materializer fixes remain covered. The rule still reports terminal `Last`/`LastOrDefault` after inline `ToList`/`ToArray`/`AsEnumerable`, but the move-before-materialization fixer is no longer offered for those ordering-sensitive terminals because moving them onto `IQueryable` can change runtime behaviour, especially unordered EF queries. Sequence continuations, safe terminal rewrites such as `Count`/`Any`, redundant materializer collapses, and shape-changing no-fix cases remain covered. |
| LC003 | Prefer Any() over Count() existence checks | Materialization & Projection | Warning | 3 | 4 | 4 | 4 | 4 | 3 | Low | **2026-07-02 constant-zero fixer guard shipped:** `Count() == Empty` where `const int Empty = 0` now rewrites to `!Any()` instead of silently inverting to `Any()`. The fixer asks the semantic model for folded zero constants, matching the analyzer's existing constant-value proof while preserving bare literal behaviour, async replacements, predicate preservation, and scalar-context coverage (boolean assignments, return expressions, async `LongCountAsync`→`AnyAsync`; 30 LC003 tests overall, 9 fixer tests). Analyzer stays 3 because the rule is intentionally limited to direct binary comparisons rather than deeper dataflow. |
| LC004 | IQueryable passed as IEnumerable | Query Shape & Translation | Warning | 4 | 4 | 3 | 4 | 4 | 4 | Low | Solid API-boundary analyzer proving foreach/forwarding/materializing sinks; 5.5.10 closed the query-expression FN. Docs/source are current and cover explicit materialization vs `IQueryable` signatures, forwarding chains, expression-bodied/query-syntax consumption, safe deferred boundaries, source-body limits, nested local-function/lambda scoping, and the narrow `.ToList()` fixer contract. Tests cover the main analyzer/fixer surfaces, including query syntax and fix-all; nested-local-function skipping stays a deliberate FP-avoidance non-goal. |
| LC005 | Multiple OrderBy calls | Query Shape & Translation | Warning | 4 | 4 | 4 | 3 | 4 | 3 | Low | Linear chain heuristic with a safe `ThenBy` fixer; query-comprehension resets report at the `orderby` clause without offering a fix. Single-assignment sorted locals are followed, including parenthesized initializers; reassigned locals, deconstruction writes, and `out`/`ref` writes stay quiet. The fixer is offered only when the receiver still has an ordered type, including static `Enumerable`/`Queryable` syntax; widened `IEnumerable<T>`/`IQueryable<T>` locals report as manual fixes. Tests remain modest: 22 total methods, including 5 dedicated fixer tests. Intervening-operator chains stay a deliberate non-goal. |
| LC006 | Multiple collection Includes | Loading & Includes | Warning | 4 | 4 | 3 | 4 | 4 | 4 | Low | High-impact sibling-collection rule; `LocalAssignmentCache` follows single-assignment locals, covering `AsSplitQuery()` across locals and sibling `Include`s split across locals. Reference-only prefixes before collection includes are treated as row-preserving, so `Address.Orders` plus `Profile.Tags` reports as sibling collection loading under the same root query. Include-path parsing uses shared `IncludePathParser`. The fixer now handles static `EntityFrameworkQueryableExtensions.Include(...)` chains by inserting `AsSplitQuery()` on the source argument instead of rewriting the extension type name. FS stays 3: `AsSplitQuery()` changes query behaviour and must be user-owned. Current coverage is 27 tests, including 9 fixer tests. Multi-reassignment chains remain an intentional non-goal. |
| LC007 | Database execution inside loop | Execution & Async | Warning | 4 | 4 | 4 | 4 | 5 | 5 | Low | Strong analyzer for proven EF execution inside loops: `Find`, explicit loads, materializers and aggregates after deferred `AsEnumerable()` boundaries, nested `foreach` source materializers that re-execute inside an outer loop, navigation `Query()`, `DbContext.Set<T>()`, single-assignment query locals, and EF set-based executors. Tests cover 25 LC007 cases: 20 analyzer and 5 fixer. The fixer remains deliberately narrow, rewriting only unconditional strongly typed explicit-loading in `foreach`/`await foreach` loops to `Include` and removing the per-item load. Docs/sample/catalog are current. |
| LC008 | Synchronous EF method in async context | Execution & Async | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **2026-07-02 awaited-receiver fixer guard shipped:** member, element, invocation, and null-conditional continuations now receive parenthesized awaited results. Static local functions inside async methods are intentionally quiet synchronous boundaries, not a live Medium follow-up. Current LC008 coverage is 29 passing net10.0 tests under the LC008/SyncBlocker filter, including 9 fixer tests; the dedicated LC008 folder has 26 tests and shared architecture/cross-analyzer coverage adds 3 more. |
| LC009 | Missing AsNoTracking in read-only path | Change Tracking & Context Lifetime | Info | 4 | 4 | 3 | 4 | 4 | 3 | Low | Recognises `DbSet` properties and generic-repository `context.Set<T>()`; the fixer lands `AsNoTracking()` on the semantic EF source. Property mutations of the materialized result — on the result local, nested members rooted in that result, foreach variables over it, or inline on the materializer (compound/increment included) — now mark the body as a write path, so helper-committed saves are no longer misadvised; DTO-mutation, read-only-access, repointed-local, and indexer-replacement guards keep the rule firing. Remaining residuals: an entity returned untouched and mutated by a caller stays invisible (documented, why severity is Info), and the identity-resolution fixer variant is still not offered (FS stays `3`). LC009 has 30 focused tests. |
| LC010 | SaveChanges inside loop | Change Tracking & Context Lifetime | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | Mature loop analyzer covering direct, `foreach`, `await foreach`, `do`, local-function-called-from-loop, local delegate and method-group calls from loops, compound delegate subscriptions, self-combining delegate assignments, loop-carried, wrapper-loop-carried, callback-loop-carried, and opposite-branch delegate assignments, local delegate aliases, local invoker callback helpers, conditional delegate initializers and invocation, setup-helper assignments including nested live helper calls, outer-delegate call chains, retry-loop guardrails, duplicate diagnostics, and fresh per-iteration contexts that should stay quiet when the saved `DbContext` local is created inside the loop body or passed through delegate parameters without parameter reassignment. Aliases to shared contexts, contexts declared in `for` initializers, direct or called-helper pre-save reassignments, delegate-parameter reassignments before saving or forwarding, duplicate delegate subscriptions with only one removal, mutable conditional or branch guard delegate paths, wrapper setup helpers before root-level loops, and delegate saves nested in real outer loops still report; delegate removals, branch-exclusive delegate paths with stable guards, branch-exclusive conditional initializer arms with stable guards, negated-guard delegate paths, branch-exiting delegate assignments, return-exiting retries, switch-local retry breaks, same-path or called-helper delegate overwrites, delegate locals reassigned before loop invocation, loop-carried assignments cleared by `break`, and loop-carried assignments overwritten before the next iteration stay quiet. The conservative do-while-only fixer keeps FixAll/CRLF coverage and refusals for unsafe loop/control-flow shapes, nested loops, multiple saves, and non-terminal saves. LC010 has 137 focused tests plus 3 cross-analyzer checks. Keep Low priority. |
| LC011 | Entity missing primary key | Schema & Modeling | Warning | 4 | 5 | 4 | 5 | 5 | 4 | Low | **2026-07-02 builder-local recursion guard shipped:** a malformed self-referential builder local in `OnModelCreating` (for example `var entity = entity.HasKey("Id");`) no longer recurses through `TryResolveLocalBuilder` until StackOverflow aborts the analysis host. Builder-expression resolution now carries a visited-expression set, so incomplete/non-compiling IDE edits are treated as unresolved while namespace-validated attributes, context-specific applied configurations, current-assembly config scanning via `typeof(LocalType).Assembly`, `System.Reflection.Assembly.GetExecutingAssembly()`, `global::System.Reflection.Assembly.GetExecutingAssembly()`, `using Assembly = System.Reflection.Assembly`, and local/inherited readonly-member aliases, inferred owned types, scoped/chained Fluent API, duplicate-safe fixes, and valid local-builder resolution remain intact; 73 LC011 tests pass on net10.0. |
| LC012 | Use ExecuteDelete instead of RemoveRange | Bulk Operations & Set-Based Writes | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | Conservative analyzer/fixer boundary is healthy after the 2026-07-04 later-save/query-source pass. LC012 reports only when EF `ExecuteDelete` is available and the single `RemoveRange` argument remains query-shaped; same-context or possibly-aliasing later saves suppress. The fixer is available for mutually exclusive branch saves and different freshly-created context saves only when the query source also resolves to the `RemoveRange` receiver context through transparent single-source LINQ/EF query operators; arbitrary helper-produced and multi-source queries stay manual. LC012 has 35 focused tests today (21 analyzer, 14 fixer). |
| LC013 | Disposed context query | Change Tracking & Context Lifetime | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | Manual-only lifetime rule with strong single-assignment/conditional/coalesce/switch coverage. It now follows only known LINQ/EF query-chain extension operators, so arbitrary project extension methods that may materialize before returning `IQueryable` stay quiet while deferred `AsEnumerable().AsQueryable()` chains still report. Coverage is 26 tests; the 49-line doc explains the custom-extension boundary and no-fixer rationale. Field/parameter origins intentionally not followed. |
| LC014 | Avoid string case conversion in queries | Query Shape & Translation | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **2026-07-02 EF async-terminal pass shipped:** EF's async predicate terminals on `EntityFrameworkQueryableExtensions` (`AnyAsync`/`AllAsync`/`CountAsync`/`FirstOrDefaultAsync`/`SingleAsync`/…) now receive the same case-conversion diagnostics as synchronous `Queryable` predicates, closing the dominant modern EF style asymmetry. EF-backed proof, local aliases, `Join`/`GroupJoin` key-selector ownership, content-carrying argument walks, and the manual-only remediation stance stay unchanged; 26 LC014 tests pass on net10.0. |
| LC015 | Missing OrderBy before pagination | Query Shape & Translation | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | High-value reliability rule with strong EF-backed source/key detection for unordered `Skip`/`Take`/`Chunk`/`ElementAt*`/`Last*` and misplaced `OrderBy` after pagination. A downstream misplaced sort no longer hides the original unordered page boundary when another pagination operator follows it, including through simple query aliases and sorted aliases. Fixer registers only when a single source-detectable key is available, and declines no-key, `[Keyless]`, multi-`[Key]`, and EF Core class-level `[PrimaryKey(...)]` composite entities. Remaining documented residual: Fluent-only composite keys can still receive a partial-key fix because the source model is invisible. Coverage: 32 LC015 tests, including 11 fixer tests. |
| LC016 | Avoid DateTime.Now/UtcNow in queries | Query Shape & Translation | Warning | 4 | 4 | 4 | 4 | 4 | 2 | Low | Clock detection across `DateTime`/`DateTimeOffset` `Now`/`UtcNow`, `IQueryable` lambdas, per-lambda dedup, unique-name statement-bodied extraction, expression-bodied method/local-function extraction, expression-bodied FixAll over multi-lambda members, void/async non-generic task conversion including aliases, trivia preservation, and unsupported expression-bodied property/static-lambda no-fix handling are covered (30 LC016 tests, 15 fixer/FixAll). Docs/README/sample frame deterministic clock boundaries, injected-clock testability, `UtcNow` preference, provider server-clock alternatives, the local-extraction contract, and the expression-bodied conversion boundary. |
| LC017 | Whole entity projection | Materialization & Projection | Info | 4 | 4 | 4 | 4 | 5 | 3 | Low | Strong 43-test heuristic with an updated doc. Analyzer remains A/FP 4: it conservatively flags large EF materializations with 1-2 locally observed property uses and skips escaping usage. The fixer builds anonymous-type projections from direct foreach properties plus supported null-conditional and indexed entity-property access shapes, so mixed downstream usage such as `e.Id` plus `e?.Name` preserves every referenced member. Indexed element escapes and cast/interface/conversion-based entity property access now withhold the fixer. |
| LC018 | FromSqlRaw with interpolated strings | Raw SQL & Security | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | Security-critical with broad call-shape coverage, an exhaustive constant-only safe-shape suite, and `SqlQueryRaw<T>` detection on the `DatabaseFacade`. **The `SqlQueryRaw<T>` fixer residual is closed (2026-06-26)**: direct interpolated scalar/keyless query SQL now rewrites to `SqlQuery<T>` while preserving generic type arguments. The fixer now only rewrites when every interpolation hole is in a likely SQL value position, and withholds safe-API rewrites for SQL identifier/structural positions such as `SELECT {columnName}`, `FROM {tableName}`, `WHERE {columnName} = 1`, or `EXEC {procedureName}`, because `FromSqlInterpolated`/`SqlQuery<T>` would parameterize the hole as a value rather than preserve SQL structure. Fix strategy stays `4` because concatenation, raw-parameter calls, quoted interpolation holes, and structural interpolation holes remain intentionally manual. Docs/README/sample now spell out the `FromSqlRaw`/`SqlQueryRaw<T>` safe-sibling split and the LC037 constructed-SQL boundary. LC018 has 41 focused tests. Demoted from Medium: the security FN shipped (LC037 owns construction sinks since 5.5.5). |
| LC019 | Conditional Include expression | Loading & Includes | Warning | 4 | 4 | 3 | 4 | 4 | 4 | Low | Manual-only rule with sound rationale (conditional Include/ThenInclude navigation choices fail at runtime). Coverage now includes 13 tests: root and receiver conditionals, coalesce, collection ThenInclude, filtered-Include predicate/order/take negatives, and non-EF Include/ThenInclude lookalikes. Docs now cover split-query, projection, eager-load-both, branch-specific filtered Include, and the filtered-Include boundary. |
| LC020 | Untranslatable string comparison overloads | Query Shape & Translation | Warning | 3 | 3 | 4 | 4 | 4 | 3 | Low | 5.5.2 closed the argument-flow FN (`"admin".Contains(u.Name, cmp)`); the Ordinal/OrdinalIgnoreCase FP claim was rejected on verification (default providers throw, so flagging is correct). Still not provider-aware (Npgsql `ILIKE`, opt-in Pomelo) and EF 9 `Collate()` is not addressed — both Analyzer and FP stay at `3`. |
| LC021 | IgnoreQueryFilters usage | Raw SQL & Security | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | LC021 remains a useful EF-only security review rule, with 23 net10.0 tests covering parameterless and named-filter extension/static diagnostics, suppressions, generated-code exclusion, and the remover fixer. The 2026-07-04 named-filter pass fixed the unsafe extension-syntax rewrite that previously replaced `query.IgnoreQueryFilters(filters)` with `filters`, and also closed the reordered named-argument static-call blind spot (`filterKeys: ..., source: ...`). Analyzer/fixer receiver selection now preserves the correct query receiver. Docs describe why named-filter bypasses still report and how the fixer handles extension vs static syntax. |
| LC022 | Nested collection materialization inside projection | Materialization & Projection | Info | 3 | 3 | 3 | 4 | 4 | 2 | Low | EF 9 correlated-collection translation has largely closed the gap this rule targeted, so the analyzer can flag patterns EF now translates correctly. Docs/README/sample now frame LC022 as an advisory query-shape review, explain modern EF correlated-collection translation, document split/direct-projection/DTO-contract choices, and spell out the conservative fixer contract. Both A/FP and Importance stay pulled down. |
| LC023 | Prefer Find/FindAsync for primary key lookups | Materialization & Projection | Info | 4 | 4 | 4 | 4 | 4 | 3 | Low | Careful key/async detection with shipped query-filter gating: visible `HasQueryFilter` entities are suppressed, including configuration-class, base-type, inherited-key, and non-generic `Entity(typeof(X))` forms. The fixer now withholds column-to-column/self-referential predicate rewrites such as `x.Id == x.OtherId`, so it does not lift lambda-parameter references into non-compiling `Find(x.OtherId)` calls. LC023 has 25 tests, including 6 fixer/FixAll cases. |
| LC024 | GroupBy with non-translatable projection | Query Shape & Translation | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | Strong fluent, query-syntax, and `GroupBy(..., (key, group) => ...)` result-selector coverage (26 tests); `Any`/`All` are recognised aggregates and an aggregate whose receiver chain roots at `g` through translatable operators (`Where`/`Select`/`OrderBy`/`Distinct`) is accepted, while non-aggregate terminals still report. Manual-only is correct — the fix depends on business intent. |
| LC025 | AsNoTracking with Update/Remove | Change Tracking & Context Lifetime | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | Sound dataflow over nearest origins, foreach paths, and explicit `Entry.State`; honours the **last** tracking directive (5.5.6), treats EF Core `AsNoTrackingWithIdentityResolution()` as no-tracking, and stays quiet on constructed-object projections. **The deferred path-insensitivity item is closed (2026-06-10)**: when the latest origin before the write is conditional relative to the use and the latest *unconditional fallback* disagrees on tracking-ness (superseded history doesn't count), the verdict is path-dependent and the rule stays quiet — while unconditional latest origins, agreeing fallbacks, and same-branch reassign+write shapes all keep firing. The fixer withholds partial rewrites for branch-conditional multi-origin no-tracking shapes where removing only the latest origin would leave another direct or query-alias no-tracking path behind. LC025 has 33 focused tests. |
| LC026 | Missing CancellationToken in async call | Execution & Async | Info | 3 | 3 | 4 | 4 | 4 | 3 | Low | **2026-07-02 chained-query fixer guard shipped:** the fixer now targets the invocation covered by the diagnostic span, so `query.Where(...).ToListAsync()` receives the token on `ToListAsync(cancellationToken)` instead of appending it to the inner `Where(...)`. Token discovery (fields/properties, `ct` preference), default/`CancellationToken.None` replacement, and named-argument preservation stay unchanged; 22 LC026 tests pass on net10.0. Analyzer/FP stay 3 because the rule deliberately avoids inferring business intent between domain-specific tokens. |
| LC027 | Missing explicit foreign key property | Schema & Modeling | Info | 4 | 4 | 4 | 4 | 4 | 3 | Low | Strong explicit-FK analyzer with good relationship-configuration coverage, including direct Fluent chains, configuration classes, string shadow FKs, single-assignment relationship-builder locals, split `WithOne`/`WithMany` continuations, shadowing, and reassignment ambiguity. The fixer now uses visible single-property Fluent `HasKey(...)` metadata before convention/key-attribute fallback, so non-conventional principal keys get the correct FK type. Coverage is 28 tests, including 5 fixer/fix-all paths. |
| LC028 | Deep ThenInclude chain | Loading & Includes | Warning | 3 | 3 | 4 | 3 | 4 | 3 | Low | Configurable-depth heuristic (editorconfig `max_depth`, default 3) with 11 test methods. Coverage now locks configured-threshold overrides, invalid-config fallback to the default threshold, sibling include-chain depth resets, and per-chain reporting when multiple sibling chains exceed the threshold. Manual-only stance is appropriate for a review-flag rule. |
| LC029 | Redundant identity Select | Materialization & Projection | Info | 4 | 4 | 4 | 3 | 4 | 2 | Low | Cosmetic cleanup rule; now covers statement-bodied interface-enumerable identity projections such as `items.Select(x => { return x; })`, keeps the fixer boundary-preserving for explicit receivers such as `AsEnumerable()`, preserves parenthesized/cast/null-forgiving fluent receivers, and skips static, concrete-enumerable, awaited-task, explicit-cast, and type-changing projection forms that the fluent fixer cannot safely rewrite. Docs clarify that intentional client/materialization boundaries should be expressed directly rather than with `Select(x => x)`. 16 tests. Low importance is the point — no further investment warranted. |
| LC030 | DbContext lifetime mismatch | Change Tracking & Context Lifetime | Info | 4 | 4 | 4 | 4 | 5 | 4 | Low | Strong manual-only lifetime rule: long-lived types proven via interface/base-class, middleware, hosted-service, singleton DI registration, direct singleton DbContext registration, and optional `long_lived_types` config. Singleton factory overloads now prove direct constructed implementation returns such as `AddSingleton<IWorker>(_ => new Worker(...))`, while DI-call recognition requires a real `IServiceCollection` extension method instead of namespace/name alone. Static `DbContext` fields/properties on proven long-lived types are reported while static storage on unproven types remains quiet. Computed getter properties that directly create a fresh context through `IDbContextFactory<TContext>.CreateDbContext()` or `new TContext()` stay quiet, while auto-properties, initialized get-only properties, and root-service-provider lookups still report. Current suite has 32 analyzer tests, no fixer, and an updated doc explaining why no single safe fix exists. Severity keeps it out of the urgent stack. |
| LC031 | Unbounded query materialization | Materialization & Projection | Info | 4 | 4 | 4 | 4 | 4 | 3 | Low | Sound chain walker, correct about `Chunk` (not a bounding operator), `TakeLast`/`SkipLast` (untranslatable, so flagging stands), and `ToLookup()` as a full collection materializer. Manual-only rationale now explains that pagination, keyset/cursor paging, exports, streaming/batching, and reviewed suppressions are product decisions the analyzer cannot choose. Coverage now includes query syntax over `DbContext.Set`, bounded query-syntax aliases, `Skip` without `Take`, `TakeLast`, transparent query options, `ToLookup`, and 23 LC031 tests. Docs/README/sample spell out non-bounds and intentional full-scan handling. |
| LC032 | ExecuteUpdate for bulk scalar updates | Bulk Operations & Set-Based Writes | Info | 4 | 4 | 4 | 4 | 4 | 3 | Low | **2026-07-02 unsupported-receiver fixer guard shipped:** receiver chains containing `Skip`, `Take`, or `Distinct` still report LC032 but no longer offer a code fix, because EF cannot translate those operators as part of an `ExecuteUpdate` receiver. The guard covers fluent chains and static `Queryable` source arguments; the async-aware fixer remains available for proven direct/filter/order chains, preserves warning comment, token propagation, and duplicate-write collapse, and now has 42 tests overall including 26 fixer tests. |
| LC033 | Use FrozenSet for static membership caches | Materialization & Projection | Info | 4 | 4 | 4 | 4 | 4 | 2 | Low | Healthy niche optimization across 7 files and 18 tests (multi-phase compilation-end analysis with strict Contains-only usage gates); low real-world impact keeps it a poor near-term investment. |
| LC034 | ExecuteSqlRaw with interpolated strings | Raw SQL & Security | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | Strong sync/async raw-SQL detection (76 tests) with named/direct unsafe SQL, lookalike negatives, supported core-library-scalar and direct single-/multi-row `INSERT ... VALUES` positives, and char/custom/generic/enum/framework-lookalike type, formatted/aligned and adjacent-hole, DML/non-DML batch/comment shape, quote/escaped-/dollar-quote, quoted-identifier apostrophe, doubled-delimiter identifier, structural table/column position, INSERT-expression, and mixed sync/async FixAll coverage. Docs now spell out LC018/LC034/LC037 ownership, direct-vs-hidden construction, quoted-interpolation limits, and parameterized `ExecuteSqlRaw` alternatives. |
| LC035 | Missing Where before bulk execute | Bulk Operations & Set-Based Writes | Info | 4 | 4 | 4 | 4 | 4 | 4 | Low | High-impact safety smell with no fixer by design. There are 34 analyzer tests. The analyzer now treats definitely assigned filtered `if`/`else` locals and all-filtered ternary/switch-expression receivers as safe, while preserving diagnostics when any branch or arm is unfiltered. Earlier base-filter, optional-narrowing, post-if/else narrowing, overwritten/stale `if`/`else` assignment, catch-path, reused filtered-local, project-local `Where`, and query-syntax boundaries remain covered. |
| LC036 | DbContext captured by thread work item | Execution & Async | Warning | 4 | 4 | 4 | 4 | 5 | 5 | Low | High-value thread-safety rule covering lambda, anonymous-method, callback, async-lambda, member capture, factory/scope-safe, materialized-value, `Parallel.For`/`ForEach`/`Invoke`, delegate-wrapped callbacks, and local-function shapes (22 tests). The method-group FN claim was rejected — arbitrary method-group inspection is a documented non-goal. Compact 49-line doc remains a DS=5 anchor (violation, safer shape, intent, explicit non-goals). |
| LC037 | Constructed raw SQL strings | Raw SQL & Security | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | Strong manual security rule (3-file analyzer: concatenation, `string.Format`/`Concat`, fluent and statement-based `StringBuilder`, aliased-local resolution; 83 tests); 5.5.5 added `SqlQueryRaw<T>` as a construction sink with no LC018 double-report. **2026-07-02 recursion guard shipped:** the valid `sql = sql + id; ExecuteSqlRaw(sql)` self-reference now reports normally instead of driving `TryResolveLocalValue` into an unbounded StackOverflow. **2026-07-03 statement-flow pass:** separate `builder.Append(...)` statements before `builder.ToString()` now report when a non-constant append can still flow into the raw SQL string, including null-conditional appends, local dynamic append values, method-call append values, loop-carried and compound-assigned append-local writes, caught-throw continuations with exact, alias, ordinary base, user-defined base, and framework base exception catches, fluent `builder.Clear().Append(...)` chains, builder-local aliases, self-preserving assignments, conditional builder value writes, conditional alias writes, copied builder expressions, and constructor copies from tainted builders, while constant-only builders, branch-selected literal append locals, path-dominated constant append-local overwrites, per-iteration constant append-local resets, variable-capacity constructors, constant compound assignments, terminating-branch local writes, alias reassignments, maybe-reassigned alias clears, short-circuit clears, short-circuit assignment resets, loop-guarded branch clears, same-loop branch exits, try/catch-contained branch clears, catch-exiting throws, catch ordering and constant-false filters that make the sink unreachable, guaranteed `finally` clears, conditional `finally` clears, early-return/loop/switch/nested terminating guard paths, only-surviving-branch clears, and guaranteed fluent or direct `Clear()` resets stay conservative. Local resolution only considers writes that completed before the value currently being inspected, and recursive local/string-builder chasing is capped, preserving prior alias/conditional-write behaviour while preventing compiler/IDE aborts. |
| LC038 | Excessive eager loading | Loading & Includes | Info | 3 | 4 | 4 | 4 | 4 | 2 | Low | Coarse but documented depth-threshold heuristic (configurable, default 4) with 10 test methods covering default/suppressing/lowering/invalid thresholds, `DbContext.Set`, transparent LINQ/EF query options, and non-EF Include lookalikes. Docs now frame the warning as a manual review prompt, explain intentional full-aggregate loads, projection/separate-query alternatives, split-query limits, and the LC006 cartesian-explosion boundary. |
| LC039 | Repeated SaveChanges on same context | Change Tracking & Context Lifetime | Info | 4 | 4 | 4 | 4 | 4 | 4 | Low | Useful reliability smell guarded for transaction boundaries (`using`/`await using` declarations included), mutually exclusive if/else branches, switch sections, switch-expression result arms, and try-vs-catch saves; 24 analyzer tests, no fixer. Docs cover batching guidance, explicit transaction boundaries, branch/try/finally behaviour, separate contexts, executable-root scoping, EF-only boundary recognition, and manual-only rationale. |
| LC040 | Mixed tracking and no-tracking modes | Change Tracking & Context Lifetime | Info | 4 | 4 | 4 | 4 | 4 | 3 | Low | Branch resolution covers if/else, switch, and ternary arms, with later shared materialization compared against reachable earlier modes; ordinary nested block cases are covered. `DbContext.Set<TEntity>()` is tracked-source evidence, matching `DbSet<TEntity>` properties when mixed with no-tracking materialization on the same context. Try/catch path modelling and conditional/loop/try local reassignment remain deliberately conservative because tracking may depend on whether a throw or reassignment actually occurred. Coverage locks EF-only tracking markers, transparent EF options, transactions, aliases, reassignments, `DbContext.Set<TEntity>()`, and separate contexts; LC040 has 19 analyzer tests and remains manual-only. |
| LC041 | Single entity over-fetches one consumed property | Materialization & Projection | Info | 4 | 4 | 4 | 4 | 4 | 2 | Low | Narrow heuristic; 5.5.12 exempted key-predicates on an upstream `Where` (same single-row-by-key fetch the terminal form exempts). Null-conditional single-property reads, scalar chains, and method chains such as `user?.Name`, `user?.Name.Length`, and `user?.Name.Trim()` count as the same one-property over-fetch and remain diagnostic-only so the fixer does not leave stale conditional-access syntax behind. The fixer handles direct `First`/`Single` and async variants, attaches to non-key chained materializers such as `Where(...).First()`, withholds optional-materializer rewrites to preserve no-row semantics, and withholds non-inline terminal predicates so hoisted entity predicates are not left on scalar projections. Remaining source-reasoned residual: hoisted `Expression<Func<>>` predicates are not resolved for key-predicate analyzer suppression. 23 tests. |
| LC042 | Complex query should be tagged | Loading & Includes | Info | 2 | 2 | 4 | 2 | 2 | 2 | Low | Crude operator-count threshold with 4 test methods; no semantic complexity distinction (`Where`+`OrderBy`+`Take` ranks the same as nested `SelectMany`+aggregates). Pure team-policy observability rule — appropriately harsh-scored, no investment warranted. |
| LC043 | Prefer await foreach over buffering async streams | Execution & Async | Info | 4 | 4 | 4 | 4 | 3 | 2 | Low | Intentionally narrow immediate-buffer-then-loop detection with proven `IAsyncEnumerable<T>` source gates; 10 test methods cover the basic list/array reports, fixer/fix-all output, cancellation-token buffer arguments, second uses, non-stream lookalikes, and nested lambda/local-function captures that must suppress the fixer. Streaming optimization, not a correctness issue. |
| LC044 | AsNoTracking entity mutated then SaveChanges | Change Tracking & Context Lifetime | Warning | 4 | 4 | 4 | 4 | 5 | 5 | Low | Reference-quality manual rule with same-context chain proof, single-assignment and reachability gates, last-tracking-directive handling, direct and nested property/indexed receiver detection with source-visible `[NotMapped]` and unconfigured-field exclusion, compound/increment mutations, and per-mutation member-path-aware tracking-state/detach/earlier-save proof. Root or matching owning/nested graph-traversing `Update` paths suppress covered mutations, while explicit Modified/Added state covers only its exact `Entry` entity path; `Attach` suppresses only before it snapshots the mutation, and every pre- or post-mutation persistence operation must complete before a save-reaching handler. Complementary and nested exhaustive `if`/`else`, guaranteed nested blocks and mandatory `do` bodies, and normal/catch paths can establish proof collectively, including before the mutation, while an optional catch-only reattach or alternate mutation-reaching handler without matching tracking cannot, while optional, sibling, branch-transfer, condition/branch-evaluation, explicit or implicit caught-exception, and later unsafe paths do not, including inside foreach loops. Collision-free constant indexer arguments distinguish sibling graph elements; unknown indices remain conservative. Manual-only remains correct because removing `AsNoTracking`, updating an existing detached mutation, and attaching before mutation have different intent. 198 analyzer tests cover direct/async materializers, nested property and indexed-member writes, unconfigured fields and explicit non-mapped graph state, exact-entity state versus graph-traversing root/nested update coverage, foreach including terminating mutation paths, same/different context, root/nested/sibling tracking paths before/after mutation, all range arguments, safe-first/unsafe-sibling ordering, optional, same-branch, complementary and nested exhaustive branches, guaranteed nested blocks and mandatory `do` bodies, throwing conditions, branch bodies, nullable instance-field reads plus current-instance and conditional-access guards, mutually exclusive implicit-throw paths, handled nested transfers before reattach, incompatible inner handlers that can still reach an outer catch, and catch paths that guarantee persistence, mandatory `do` versus optional `while`, first-iteration `do` transfers, caught-throw-skipped complementary, fully covered try/catch, explicit/implicit and nested try-to-catch/finally saves, catch-only persistence, invocation/property/indexer exception bypass before or after mutation, caught-throw-skipped catch updates, harmless finally, finally-clear invalidation, nearest-inner-catch handling, narrow/base catch overlap, non-constant-filter propagation, constant-true-filter termination, conditional/coalescing throw expressions before and inside mutations, switch-expression arm exclusivity plus governing-expression order, exact and open exception channels, mixed replacement/safe-rethrow propagation, incompatible terminal nested tries, uninvoked nested executable isolation, precise break/continue/goto targets, post-try, constant-false-filter, non-matching, exclusive, return, and goto-past-save safety, unreachable earlier saves, conditional and guarded pre-attach including loop guards, stale attach after explicit detach/tracker clear before save that must still report, ambiguous reassignments, and nested-block reachability. |
| LC045 | Missing Include — navigation on materialized entity | Loading & Includes | Warning | 4 | 3 | 4 | 4 | 4 | 5 | Low | **New in 5.6.0; origin-aware control flow hardened 2026-07-09 and exact query-consumer surface completed 2026-07-13.** The analyzer follows each materializer and extracted entity origin through forward control flow, versions local/alias bindings, carries reference-navigation prefixes, intersects definitely-written paths at joins, unions origin uncertainty, and preserves diagnostics found before uncertainty. It now also resolves exact static/named query sources, tracks supported `ToHashSet*` and query-root `ElementAt*` overloads, recognises direct property subpatterns, and analyses supported callback CFGs only while the original collection generation is active. Custom and framework-namespace lookalikes, multi-source operators, effectful callback chains, entity-returning projections, temporal APIs, async streams, and repository roots remain conservative. The focused net10.0 slice passes **300 tests**. Scores stay unchanged: exact top-level `OnModelCreating` `AutoInclude()` evidence is now recognised, while indirect/transitive configuration remains unproven and null-guarded access deliberately fires. |

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

### 2026-07-04 45-rule current-state audit

One read-only reviewer inspected each rule against current source, tests, docs, samples, and the candidate queue. This pass updates the scorecard to represent today's planning state. Some rows are provisionally demoted for source-reviewed fixer-safety, test, or documentation risk; that is not the same as declaring the candidate lead a verified defect.

| Rule | Change | Why |
| --- | --- | --- |
| LC002 | **FS 4→2, T 4→3, Priority Low→High** | Current fixer coverage still misses terminal `Last`/`LastOrDefault` after inline materializers. Moving those calls back to `IQueryable` can change runtime behaviour, especially for unordered EF queries. |
| LC012 | **FS 4→3, T 4→3, Priority Low→Medium** | Analyzer parity is strong, but current-state review found two fixer risks: later `SaveChanges` suppressed fixes too broadly, and cross-context query-source safety was not proven. Both are fixed in the LC012 pass below. |
| LC016 | **T 4→3** | Expression-bodied members could receive a code action that no-ops because the fixer required an enclosing statement. Fixed in the LC016 expression-bodied fixer guard pass below. |
| LC017 | **FS 3→2, Priority Low→Medium** | The fixer collects projection members more narrowly than the analyzer's usage proof, so mixed access shapes can leave omitted properties after the rewrite. |
| LC021 | **T 4→3, DS 4→3, Priority Low→Medium** | Named-filter `IgnoreQueryFilters(filterKeys)` was not covered in tests or docs, and the then-current fixer argument selection was unsafe for extension syntax with arguments. Fixed in the LC021 named-filter fixer guard pass below. |
| LC023 | **FS 4→3, T 4→3, Priority Low→Medium** | The shipped query-filter suppression is current, but a self-referential predicate fixer lead remains open and needs a red test before repair. |
| LC027 | **FS 4→3, Priority Low→Medium** | Fixer key-type inference still depends on `[Key]`, `Id`, or `{Type}Id`; fluent-only non-conventional primary keys can infer the wrong FK type. |
| LC035 | **FP 4→3, T 4→3, DS 4→3, Priority Low→Medium** | Two current source-reasoned FP leads remain for definitely assigned filtered locals and all-filtered conditional receivers; keep demoted until confirmed and fixed or rejected. |
| LC004, LC005, LC006, LC007, LC008, LC010, LC015, LC030, LC039, LC040, LC044 | Wording/count refresh only | Rows now reflect current doc line counts, test counts, shipped guardrails, and which residuals are deliberate vs unverified. |
| LC001, LC003, LC011, LC013, LC014, LC018-LC020, LC022, LC024-LC026, LC028-LC034, LC036-LC038, LC041-LC043, LC045 | No score change | Reviewers found the scorecard current enough for planning; candidate-queue leads remain below for future red-test confirmation. |

### 2026-07-04 LC002 `Last*` fixer guard pass

| Rule | Change | Why |
| --- | --- | --- |
| LC002 | **FS 2→4, T 3→4, Priority High→Low** | Test-confirmed the 2026-07-04 current-state lead: `db.Users.ToList().Last()` and `LastOrDefault()` received the `Move query operator before materialization` code action and rewrote to `db.Users.Last*()`. The analyzer still reports the premature boundary, but the fixer now withholds the unsafe rewrite for `Last`/`LastOrDefault`, preserving existing safe sequence and terminal rewrites. LC002 now has 47 tests, including 16 fixer tests. |

### 2026-07-04 LC021 named-filter fixer guard pass

| Rule | Change | Why |
| --- | --- | --- |
| LC021 | **FS 3→4, T 3→4, DS 3→4, Priority Medium→Low** | Test-confirmed the 2026-07-04 current-state lead: `query.IgnoreQueryFilters(filters)` received a code action that rewrote the query to `filters`, breaking semantics and usually changing the result type. The pass also confirmed that reordered named static syntax (`IgnoreQueryFilters(filterKeys: filters, source: query)`) stayed silent because receiver detection looked at the first syntactic argument. The analyzer/fixer now choose the source query for reduced extension calls, positional static calls, and named static calls. Added named-filter analyzer and fixer coverage for extension, static, and reordered named static syntax, and updated docs/README guidance for the named-filter boundary. LC021 now has 23 tests. |

### 2026-07-04 LC012 later-save/query-source fixer pass

| Rule | Change | Why |
| --- | --- | --- |
| LC012 | **FS 3→4, T 3→4, Priority Medium→Low** | Test-confirmed that the analyzer reported `RemoveRange(query)` while the fixer was withheld whenever any later `SaveChanges()` appeared, even when that save could never commit the removals. The fixer now mirrors the analyzer's later-save model: mutually exclusive `if`/`else` and `switch` branches do not suppress the code action, and saves on different freshly-created context locals do not suppress it when the query source also resolves to the `RemoveRange` receiver context through transparent single-source LINQ/EF query operators. Review-confirmed cross-context regressions (`query` from the later-save context, from an arbitrary helper that could return it, or from a multi-source composition such as `Concat`) now stay manual. Added fixer regressions for all five shapes while preserving suppression for same-context or possibly-aliasing saves. LC012 now has 35 tests. |

### 2026-07-04 LC023 self-referential predicate fixer pass

| Rule | Change | Why |
| --- | --- | --- |
| LC023 | **FS 3→4, T 3→4, Priority Medium→Low** | Test-confirmed the current-state lead: `users.FirstOrDefault(x => x.Id == x.OtherId)` reported LC023 and offered a fixer that rewrote to `users.Find(x.OtherId)`, referencing the lambda parameter outside the lambda and breaking the build. The fixer now declines when the selected key value expression still references the predicate parameter, leaving column-to-column predicates diagnostic-only while preserving ordinary local/parameter key rewrites, awaited async rewrites, cancellation-token preservation, and FixAll coverage. LC023 now has 25 tests. |

### 2026-07-04 LC027 Fluent key-type fixer pass

| Rule | Change | Why |
| --- | --- | --- |
| LC027 | **FS 3→4, T 3→4, Priority Medium→Low** | Test-confirmed the current-state lead: when `Customer` used a visible non-conventional Fluent key such as `modelBuilder.Entity<Customer>().HasKey(x => x.Code)`, the fixer still inserted `int CustomerId` because it only considered `[Key]`, `Id`, or `{Type}Id` primary-key discovery. The fixer now scans visible single-property Fluent `HasKey(...)` configuration for the navigation type before falling back to convention/key attributes, so it emits the configured key's type while preserving convention, non-`int`, nullable optional-navigation, and FixAll coverage. LC027 now has 28 tests. |

### 2026-07-04 LC017 projection-member fixer pass

| Rule | Change | Why |
| --- | --- | --- |
| LC017 | **FS 2→4, Priority Medium→Low** | Test-confirmed the current-state lead: when a materialized entity was used through both direct foreach access and a supported null-conditional access such as `e.Id` plus `e?.Name`, the fixer projected only the direct member and left post-fix code referencing an omitted anonymous-type property. The fixer now includes direct foreach, null-conditional, and indexed entity-property access shapes when constructing the anonymous projection, while withholding indexed fixes if the indexed entity also escapes. Explicit-type no-fix and FixAll behaviour remain intact. LC017 now has 40 dedicated tests. |

### 2026-07-04 LC035 filtered-path pass

| Rule | Change | Why |
| --- | --- | --- |
| LC035 | **FP 3→4, T 3→4, DS 3→4, Priority Medium→Low** | Test-confirmed both current-state leads: a local definitely assigned through filtered `if`/`else` branches still reported because LC035 required a latest unconditional filtered base, and all-filtered ternary/switch-expression receivers still reported because receiver analysis did not descend into conditional arms. LC035 now treats complete filtered `if`/`else` assignments and all-filtered conditional/switch receivers as safe, while branch/arm variants with any unfiltered query still report. Regression coverage also locks reused filtered locals across optional reassignments and conditional/switch arms, overwritten earlier assignments before a later complete filtered `if`/`else`, optional filtered/unfiltered reassignments after that complete branch base, and stale complete `if`/`else` candidates overwritten by a later filtered complete branch. Docs and README now spell out the every-path contract. LC035 now has 34 tests. |

### 2026-07-04 LC016 expression-bodied fixer guard pass

| Rule | Change | Why |
| --- | --- | --- |
| LC016 | **FS 3→4, T 3→4** | Test-confirmed the current-state lead: an expression-bodied query method received the `Extract to local variable` action but the fixer returned the original arrow-bodied method unchanged because no enclosing statement existed. The fixer now converts expression-bodied methods and local functions to block bodies, inserts captured clock locals before the rewritten expression or return, preserves void and async non-generic task shapes including aliases, rewrites every non-static query-lambda clock access in the same expression-bodied member for FixAll, and preserves expression trivia while leaving expression-bodied properties/indexers and static query lambdas manual without no-op or non-compiling code actions. LC016 now has 30 tests. |

### 2026-07-04 LC015 downstream-sort pagination guard pass

| Rule | Change | Why |
| --- | --- | --- |
| LC015 | No score change | Test-confirmed the current-state lead: `db.Users.Skip(10).OrderBy(...).Take(5)`, the equivalent page-alias shape, and a sorted-alias continuation reported only the misplaced downstream `OrderBy`, because any downstream sort suppressed the missing-order diagnostic on the earlier `Skip`. LC015 now ignores a downstream misplaced sort as protective when another pagination operator follows it, so the unordered page boundary is reported while the existing misplaced-sort diagnostic stays intact. LC015 now has 32 tests. |

### 2026-07-04 LC045 queryable-source fixer guard pass

| Rule | Change | Why |
| --- | --- | --- |
| LC045 | No score change | Test-confirmed the current-state lead: when a DbSet-rooted query was widened to `IEnumerable<T>` before materialization, LC045 correctly reported the missing Include but still offered a fixer that rewrote `source.ToList()` to non-compiling `source.Include(...).ToList()`. The fixer now registers only when the source expression it would wrap is statically `IQueryable<T>`, preserving safe DbSet/queryable fixes while leaving widened enumerable aliases diagnostic-only. LC045 now has 71 tests. |

### 2026-07-04 LC044 prior-reattach guard pass

| Rule | Change | Why |
| --- | --- | --- |
| LC044 | No score change | Test-confirmed the current-state lead: an entity loaded with `AsNoTracking()` and then re-attached before a property mutation still reported as a silent write, even though EF tracks the later mutation. LC044 now suppresses when a same-context `Attach`/`Update`/`Entry(entity).State = Modified|Added` dominates the mutation path and remains effective through `SaveChanges`, while optional pre-attach branches, reachable explicit detach, and reachable `ChangeTracker.Clear()` still report because another path or stale attach can mutate and save the entity untracked. LC044 now has 51 tests. |

### 2026-07-04 LC007 deferred AsEnumerable aggregate pass

| Rule | Change | Why |
| --- | --- | --- |
| LC007 | No score change | Test-confirmed the current-state lead: a provably EF-backed query followed by deferred `AsEnumerable()` and then a terminal aggregate inside a loop stayed silent because provenance stopped at the client boundary. LC007 now follows through deferred `AsEnumerable()` when the later terminal execution is what makes the query run, while in-memory `List<T>.AsEnumerable().Count(...)` stays quiet. LC007 now has 24 tests. |

### 2026-07-04 LC036 Parallel API capture pass

| Rule | Change | Why |
| --- | --- | --- |
| LC036 | No score change | Test-confirmed the current-state leads: `DbContext` captures in `Parallel.For(...)`, `Parallel.Invoke(...)`, and delegate-wrapped callbacks such as `Task.Run(new Action(() => ...))` stayed silent even though equivalent direct callbacks already reported the same unsafe cross-thread capture. LC036 now treats `Parallel.For`, `Parallel.ForEach`, `Parallel.Invoke`, and delegate-wrapped callback expressions consistently while preserving the safe shape where the callback creates its own context. LC036 now has 22 tests. |

### 2026-07-04 LC010 scoped-receiver fixer guard pass

| Rule | Change | Why |
| --- | --- | --- |
| LC010 | No score change | Test-confirmed the current-state fixer lead: a terminal `db.SaveChanges()` in a `do` loop received the move-after-loop code action even when `db` was declared inside the loop body, producing non-compiling code that referenced an out-of-scope local. The fixer now asks the semantic model for the save receiver and withholds the code action when that receiver is declared inside the loop being moved out of, while preserving the existing safe `do`-loop moves. LC010 now has 31 dedicated tests. |

### 2026-07-05 LC025 identity-resolution no-tracking pass

| Rule | Change | Why |
| --- | --- | --- |
| LC025 | No score change | Test-confirmed the current-state false-negative lead: entities materialized with EF Core `AsNoTrackingWithIdentityResolution()` stayed silent when later passed to `Update(...)`, even though identity resolution still leaves the entity untracked. LC025 now treats that EF Core directive like `AsNoTracking()` for analyzer provenance and removes it in the existing fixer path. LC025 now has 31 focused tests. |

### 2026-07-05 LC025 multi-origin fixer guard pass

| Rule | Change | Why |
| --- | --- | --- |
| LC025 | No score change | Test-confirmed the current-state unsafe-fix lead: when every path yielded a no-tracking entity but the latest no-tracking assignment was branch-conditional over an earlier direct or query-alias no-tracking fallback, the fixer removed only the branch origin and left another no-tracking path behind. LC025 now reports that diagnostic without a code fix unless the selected origin is a single safe rewrite target. LC025 now has 33 focused tests. |

### 2026-07-05 LC024 GroupBy result-selector pass

| Rule | Change | Why |
| --- | --- | --- |
| LC024 | No score change | Test-confirmed the current-state false-negative lead: `Queryable.GroupBy(keySelector, (key, group) => ...)` result selectors were never inspected because LC024 only entered through a following `.Select(...)` call. The analyzer now reuses the same projection walker for result selectors, with the second lambda parameter treated as the grouped sequence. LC024 now has 26 focused tests. |

### 2026-07-05 LC031 ToLookup materializer pass

| Rule | Change | Why |
| --- | --- | --- |
| LC031 | No score change | Test-confirmed the current-state false-negative lead: `ToLookup()` fully materializes and groups the query client-side, but LC031 did not classify it as a collection materializer. LC031 now reports unbounded EF-backed `ToLookup()` calls alongside the existing `ToList`/`ToArray`/`ToDictionary`/`ToHashSet` materializers. LC031 now has 23 focused tests. |

### 2026-07-05 LC040 DbContext.Set tracked-source pass

| Rule | Change | Why |
| --- | --- | --- |
| LC040 | **A 3→4** | Test-confirmed the current-state false-negative lead: tracked materialization through `DbContext.Set<TEntity>()` was ignored when proving the tracking mode, so a method could mix `db.Set<User>().ToList()` and `db.Users.AsNoTracking().ToList()` on the same context without an LC040 diagnostic. LC040 now treats `DbContext.Set<TEntity>()` as tracked query evidence while preserving the existing no-tracking marker handling and context-symbol resolution. LC040 now has 19 focused tests. |

### 2026-07-05 LC001 nested lambda and fixer safety pass

| Rule | Change | Why |
| --- | --- | --- |
| LC001 | No score change | Test-confirmed both current-state leads: a helper call inside a nested query lambda was missed when it depended only on an outer query range variable, and the fixer rewrote only the inner correlated query boundary by inserting `.AsEnumerable()` on the nested source. LC001 now checks helper dependencies against the lambda that owns each candidate query invocation, and withholds the fixer for diagnostics owned by nested query invocations so correlated subqueries stay diagnostic-only instead of receiving a misleading partial client-evaluation rewrite. LC001 now has 42 focused tests. |

### 2026-07-05 LC007 nested foreach source pass

| Rule | Change | Why |
| --- | --- | --- |
| LC007 | No score change | Test-confirmed the current-state false-negative lead: EF query execution used as the source of an inner `foreach` was checked only against that nearest inner loop, where the invocation is outside the loop body, and was never attributed to the surrounding outer loop where the inner source is re-executed per iteration. LC007 now walks outward to the nearest loop for which the invocation is actually per-iteration work while preserving executable-root checks that keep lambdas declared inside loops quiet. LC007 now has 25 focused tests. |

### 2026-07-05 LC006 reference-prefixed sibling pass

| Rule | Change | Why |
| --- | --- | --- |
| LC006 | No score change | Test-confirmed the current-state false-negative lead: LC006 grouped collection siblings by every preceding segment name, including reference navigations, so `Address.Orders` and `Profile.Tags` were treated as unrelated parent groups even though references do not multiply root rows. LC006 now groups by prior collection ancestors only and uses the reference-prefixed collection path as the sibling identity when needed. LC006 now has 24 focused tests. |
| LC018 | No score change | Test-confirmed the current-state unsafe-fix lead: `FromSqlRaw($"SELECT * FROM {tableName}")` still deserves LC018, but the safe-API fixer rewrote it to `FromSqlInterpolated(...)`, which parameterizes `tableName` as a value instead of preserving a reviewed SQL identifier/fragment. LC018 now suppresses its fixer unless every interpolation hole is in a likely SQL value position, preserving the existing comparison-value fix while leaving identifier, column, predicate, stored-procedure, dotted, ordering, and other structural fragments manual. LC018 now has 41 focused tests. |
| LC010 | No score change | Test-confirmed the current-state false-positive lead: a `DbContext` declared inside the loop body was still reported even though each iteration creates its own context and moving the save outside the loop would be invalid. LC010 now suppresses saves whose receiver local is declared inside the same loop body, while preserving diagnostics for shared contexts declared outside the loop. LC010 now has 36 focused tests. |

### 2026-07-08 LC030 singleton registration precision pass

| Rule | Change | Why |
| --- | --- | --- |
| LC030 | No score change | Test-confirmed a singleton factory false negative and a namespace-only false positive: `AddSingleton<IWorker>(_ => new Worker(...))` did not prove `Worker` was long-lived, while any `Microsoft.Extensions.DependencyInjection` helper named `AddSingleton` could mark a type long-lived even when the receiver was not `IServiceCollection`. LC030 now recognises direct constructed implementation returns from singleton factories, requires DI extension methods to target `IServiceCollection`, and reports multiple stored `DbContext` candidates in deterministic source order. LC030 now has 32 focused tests. |

### 2026-07-05 LC030 static DbContext storage pass

| Rule | Change | Why |
| --- | --- | --- |
| LC030 | No score change | Test-confirmed the current-state false-negative lead: static `DbContext` fields and properties on proven long-lived types were skipped before LC030 could consider them, even though static storage is long-lived by definition. LC030 now feeds static fields/properties through the same long-lived-type proof gate as instance members, preserving quiet behaviour for static storage on types with no long-lived evidence. LC030 now has 25 focused tests. |

### 2026-07-05 LC013 custom extension boundary pass

| Rule | Change | Why |
| --- | --- | --- |
| LC013 | No score change | Test-confirmed the current-state false-positive lead: a project extension method that materializes with `ToList()` before returning `IQueryable<T>` was treated like a transparent query-chain operator, so LC013 reported a disposed-context query even though enumeration no longer depends on the disposed context. LC013 now continues through known LINQ/EF query-chain extension operators only, preserving diagnostics for deferred wrappers such as `AsEnumerable().AsQueryable()` while treating arbitrary project extensions as conservative boundaries. LC013 now has 26 focused tests. |

### 2026-07-05 LC039 switch-expression branch pass

| Rule | Change | Why |
| --- | --- | --- |
| LC039 | No score change | Test-confirmed the current-state false-positive lead: `SaveChanges()` calls in different switch-expression result arms were treated as repeated saves even though only one arm executes. LC039 now treats distinct result arms as mutually exclusive while preserving diagnostics for repeated saves inside the same arm and for guard saves that can run before matching continues to a later arm. LC039 now has 24 focused tests. |

### 2026-07-05 LC011 current-assembly configuration scan pass

| Rule | Change | Why |
| --- | --- | --- |
| LC011 | No score change | Test-confirmed the current-state false-positive lead: `ApplyConfigurationsFromAssembly(System.Reflection.Assembly.GetExecutingAssembly())`, or a local/readonly-member alias of that current assembly, was not treated like `typeof(LocalType).Assembly`, so visible `IEntityTypeConfiguration<TEntity>` key configuration could be ignored and LC011 could report incorrectly. LC011 now recognises those current-assembly expressions while keeping external assembly arguments quiet, including `global::System.Reflection.Assembly`, `using Assembly = System.Reflection.Assembly`, unqualified `Assembly` calls shadowed by current/parent-namespace types, local/member/foreach/catch/pattern values, or non-System aliases, mutable member aliases, inherited readonly member aliases, derived mutable members that shadow inherited readonly aliases, and assignments that exist only inside uninvoked nested executable bodies. LC011 now has 73 focused tests. |

### 2026-07-05 LC030 computed property guard pass

| Rule | Change | Why |
| --- | --- | --- |
| LC030 | No score change | Test-confirmed the current-state false-positive lead: computed `DbContext` properties on proven long-lived types were treated like stored context properties even when the getter directly creates a fresh context through `IDbContextFactory<TContext>.CreateDbContext()` per access. LC030 now ignores expression-bodied and block-bodied computed get-only `DbContext` properties only when the getter returns a known-fresh factory/constructor expression, while stored auto-properties, initialized get-only properties, and root-service-provider lookups still report. LC030 now has 29 focused tests. |

### 2026-07-05 LC041 materializer fixer safety pass

| Rule | Change | Why |
| --- | --- | --- |
| LC041 | **FS 3→4, T 3→4, DS 3→4** | Test-confirmed both current-state fixer leads: chained non-key materializers such as `users.Where(x => x.IsActive).First()` reported LC041 but did not receive the safe scalar-projection fix, and hoisted terminal predicates such as `users.First(active)` received a non-compiling fix that left an entity predicate on the projected scalar materializer. LC041 now binds the code fix to the terminal diagnostic invocation, projects after existing query steps, and withholds fixes for non-inline predicate arguments. LC041 now has 23 focused tests. |

### 2026-07-05 LC017 conversion-access no-fix pass

| Rule | Change | Why |
| --- | --- | --- |
| LC017 | No score change | Test-confirmed the narrower current-state fixer-safety lead: when downstream usage mixed a supported direct access such as `e.Id` with cast/interface/conversion-based access such as `((IHasName)e).Name` or `((IHasName)e)?.Name`, the fixer projected only the supported direct member and left code depending on the original entity shape. LC017 now withholds the anonymous-projection fixer for conversion-based entity property access while keeping the diagnostic. LC017 now has 43 focused tests. |

### 2026-07-05 LC009 nested-member mutation pass

| Rule | Change | Why |
| --- | --- | --- |
| LC009 | No score change | Test-confirmed the final current-state candidate: `user.Profile.DisplayName = name` after materialization was still reported as a read-only path because mutation detection recognised only direct entity-local property writes. LC009 now treats property-reference chains rooted in a materialized entity local as write paths while preserving DTO, repointed-local, and indexer-replacement guardrails. LC009 now has 30 focused tests. |

### 2026-07-09 LC045 origin-aware control-flow pass

| Rule | Change | Why |
| --- | --- | --- |
| LC045 | No score change | One hundred three red/green adversarial tests confirmed that the prior all-or-nothing usage scan let a later escape or reassignment erase an earlier missing-`Include` read and let one-branch, stale-alias, or different-entity navigation writes suppress unrelated reads. LC045 now follows each materializer and extracted entity origin through forward control flow, versions local/alias bindings, carries reference-navigation prefixes, intersects definitely-written paths at joins, unions origin uncertainty, and preserves diagnostics found before uncertainty. Conservative guardrails lock subsequent escape/repoint suppression, all-branch and same-generation writes, sibling and rebound aliases, conditional/deconstruction stores, composite helper and constructor arguments, direct-index/local identity, prefix-scoped escapes, deferred captures, loop back-edges, and exact `ConfigureAwait` wrapping. All 76 prior behavioural tests remain green, for 179 focused tests total; the documented `AutoInclude()` gap keeps the scores unchanged. |

### 2026-07-10 LC045 synchronous foreach and element-extraction pass

| Rule | Change | Why |
| --- | --- | --- |
| LC045 | No score change | Forty-two red/green cases confirmed that LC045 was silent for inline `ToList()` foreach, direct and hoisted DbSet/IQueryable foreach roots, nested collection reads such as `Items.Product`, and exact `System.Linq.Enumerable` extraction from a materialized collection. The existing CFG now binds direct-loop and nested-collection origins, preserves composed navigation prefixes, and keeps custom lookalikes, predicate/default-value overloads, repository parameters, async streams, and widened `IEnumerable` sources conservative. Additional locations normalize to the exact query source so the fixer safely wraps either materializer or direct-loop expressions. The focused net10.0 LC045 slice passes 221 tests, the broad LC045-plus-architecture filter passes 239, and the full net10.0 suite passes 1,853; scores remain unchanged because the documented `AutoInclude()` boundary is still intentionally conservative. |

### 2026-07-13 LC045 exact query-consumer surface pass

| Rule | Change | Why |
| --- | --- | --- |
| LC045 | No score change | Red/green coverage confirmed semantic source-parameter resolution for reordered static calls, exact Queryable/EF/relational query-preserving steps (`AsQueryable`, `IgnoreAutoIncludes`, `FromSql*`), supported `ToHashSet*` and query-root `ElementAt*` overloads, static Include argument binding, direct property subpatterns, and nested CFG analysis of direct inline `List<T>.ForEach` plus single-source `Enumerable.Where`/`Select`/`Any`/`All` callbacks. Callback analysis now requires the original materialized collection generation to be active at the invocation; effectful `Where` predicates do not forward provenance, scalar `Select` projections do not poison later reads, and identity/entity-returning projections remain conservative. Exact framework symbols reject namespace lookalikes. The focused net10.0 LC045 slice passes **280 tests**, the broad LC045-plus-architecture filter passes **537**, and the full net10.0 suite passes **1,912**; scores remain unchanged because this is precision depth, not evidence that the documented `AutoInclude()` boundary is resolved. |

### 2026-07-15 LC045 model-level AutoInclude precision pass

| Rule | Change | Why |
| --- | --- | --- |
| LC045 | No score change | A red regression confirmed that LC045 reported `Customer` after the exact queried context configured `modelBuilder.Entity<Order>().Navigation(o => o.Customer).AutoInclude()`. LC045 now caches exact top-level model-level eager-loading evidence per context and entity path for both materializers and direct synchronous enumeration, including constructed generic contexts. Twenty regressions cover the positive materializer/foreach paths and keep query-level `IgnoreAutoIncludes()`, fluent, explicit, conditional, or runtime-valued disablement, early-exit, conditional-expression, or deferred calls, later base/helper configuration boundaries including single- and multi-assignment builder aliases, shadowed or hidden-slot `OnModelCreating` lookalikes, and different contexts/navigation paths diagnostic. The focused net10.0 LC045 slice passes **300 tests** and the full net10.0 suite passes **1,932**; scores remain unchanged because indirect builder/configuration-class and transitive auto-include proof remains deliberately conservative. |

### 2026-07-16 LC034 structural SQL fixer guard pass

| Rule | Change | Why |
| --- | --- | --- |
| LC034 | No score change | A red fixer regression confirmed that direct structural interpolation such as `ExecuteSqlRaw($"DELETE FROM {tableName}")` received an `ExecuteSql(...)` rewrite even though database parameters cannot represent SQL identifiers. LC034 now limits automatic fixes to proven core-library scalar parameters in unambiguous single-statement `UPDATE`/`DELETE`/`INSERT` value positions. Review then closed unsafe boundaries around ambiguous reference, char, and custom/generic/enum/framework-lookalike value types, formatted/aligned and adjacent holes, provider comments, DML and non-DML batch/multi-statement commands, provider-style backslash-escaped quotes, PostgreSQL dollar-quoted literals, and bracketed, double-quoted, or backtick-delimited identifiers including apostrophes and doubled delimiter escapes. Complete direct scalar `INSERT ... VALUES (...)` rows retain fixes, including multiple rows, while SQL expressions and value-type table/column holes stay manual; mixed sync/async FixAll shares one action identity. Ambiguous shapes remain diagnostic-only. The focused net10.0 LC034 slice passes 76 tests. |

### 2026-07-16 LC044 nested graph and path-proof pass

| Rule | Change | Why |
| --- | --- | --- |
| LC044 | No score change | Red regressions confirmed that nested property writes were missed and that initial suppression could let a sibling operation, an optional branch/loop operation, or a caught transfer hide an unsafe mutation. LC044 now records root-to-leaf graph paths per mutation and proves tracking-state, detach, and earlier-save coverage on each mutation-to-save path, including precise branch targets, caught exception types/filters, post-try operations, and handler exits. Review added collective proof for complementary and nested exhaustive `if`/`else` branches, guaranteed nested blocks and mandatory `do` bodies, and normal/catch paths, then corrected the EF state boundary: `Update`/`UpdateRange` traverse the targeted graph, explicit Modified/Added/Detached state affects only the exact `Entry` entity, and `Attach`/`AttachRange` are safe only before mutation. Paired root/nested update regressions preserve graph-prefix coverage. Further regressions cover every range argument, first-iteration `do` flow without bypassing transfers, catch-only persistence followed by compatible rethrow, base-typed throws crossing narrow catches, terminal nested replacement throws, independent replacement/rethrow channels, explicit and implicit try-to-catch/finally saves, terminating foreach mutation paths, collision-free constant indexed graph elements, throwing condition and covered-branch evaluations, and calls/getters/indexers or nullable instance-field reads and catch-side persistence calls themselves that can bypass tracking into a fall-through handler, with current-instance, conditional-access, and mutually exclusive sibling operations kept quiet. Inner handlers that consume an implicit transfer and resume before branch persistence no longer contaminate a reachable outer catch, while incompatible inner handlers remain diagnostic and catch paths that guarantee persistence remain safe. Unconfigured field-only receiver paths remain quiet; optional outer branches, caught transfers before persistence, finally clear, and filter propagation cannot over-suppress. The focused net10.0 LC044 slice passes 186 tests and the full net10.0 suite passes 2,114 tests. |

### Candidate queue — source-reasoned, NOT yet test-confirmed

The 2026-07-02 adversarial review and 2026-07-04 per-rule current-state audit surfaced the findings below by reasoning from source. They passed review/skeptic scrutiny but were **not** individually reproduced against the built analyzer unless a later section says so, so they are leads for the next hardening pass, **not** verified defects. Scorecard demotions based on these leads are planning-risk demotions only; construct a red test and confirm each before treating it as a shipped defect or implementing a fix. (This is exactly how several 2026-06-04 probe claims were later *rejected* on verification.)

| Rule | Class | Lead (unverified) |
| --- | --- | --- |
| None | — | No source-reasoned current-state candidates remain after the LC009 nested-member mutation pass. |


## Planning Shortlist

Work flows to rules that are high in the Importance Ranking **and** carry health gaps.

| Priority | Rules | Work |
| --- | --- | --- |
| High — current-state lead | None | The only High lead, LC002 `Last`/`LastOrDefault` fixer safety, is test-confirmed and fixed in the 2026-07-04 LC002 guard pass. |
| Medium — current-state leads | None | LC017 and LC035 are test-confirmed and fixed in the 2026-07-04 guard passes. |
| Low — opportunistic hygiene | Candidate queue | LC045 origin-aware control flow, LC016 expression-bodied fixer no-op, LC015 downstream-sort suppression, LC045 widened-enumerable fixer safety, LC044 prior-reattach suppression, LC007 deferred `AsEnumerable()` execution detection, LC007 nested-foreach source execution detection, LC006 reference-prefixed sibling detection, LC018 SQL identifier-position fixer safety, LC036 Parallel/delegate callback capture detection, LC010 scoped-receiver fixer safety, fresh-context-per-iteration suppression, local-delegate loop invocation detection, and self-combining delegate preservation, LC025 identity-resolution no-tracking detection and multi-origin fixer safety, LC024 GroupBy result-selector detection, LC031 `ToLookup()` materializer detection, LC040 `DbContext.Set<TEntity>()` tracked-source detection, LC001 nested-lambda/fixer-safety guards, LC009 nested-member mutation guard, LC017 conversion-access no-fix guard, LC041 chained-materializer fixer and hoisted-predicate no-fix guard, LC030 static DbContext storage detection and computed-property guard, LC013 custom extension boundaries, LC039 switch-expression branch guards, and LC011 current-assembly configuration scanning are test-confirmed and fixed. No source-reasoned current-state findings remain in the queue. |

Rejected/deferred-by-design items (LC004 nested-local-function, LC036 method-group, LC040 try/catch + `Select`, LC044 untaken-branch re-attach, LC020 Ordinal flagging, LC015 `TakeLast`/`SkipLast`, LC031 `TakeLast`, and the old LC021 claim that selective filter keys are harmless false positives) stay closed — do not re-chase without new evidence. The 2026-07-04 LC021 Medium item was different and is now fixed: named-overload fixer/docs/test coverage. See the 2026-06-04 Rerun tables below for full rationale.

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
| LC044 | Re-attach inside an untaken branch suppresses (FN) | **Fixed 2026-07-16.** Optional pre- or post-mutation tracking operations no longer suppress; persistence proof must dominate or collectively cover every mutation-to-save path. |
| LC012 | SaveChanges on a *different* context / mutually-exclusive branch suppresses the fixer even though the analyzer reports | **Fixed 2026-07-04.** The fixer now mirrors analyzer reachability and same-instance proof for later saves. |
| LC023 | `FirstOrDefault(pk)` → `Find` changes results under a global query filter | **Deferred → Medium (top of the verify queue).** Narrowed on review: `Find` applies query filters on its database query but returns an already-tracked filtered-out instance from the change tracker; confirm against EF Core 9 then gate on `HasQueryFilter`. |
| LC009 | Tracked mutation persisted via a helper / cross-method SaveChanges (FP) | **Fixed 2026-06-10; nested-member residual fixed 2026-07-05.** Property mutations of the materialized entity now mark the body as a write path for direct result locals, nested members rooted in those locals, foreach variables over the result, inline materializer property writes, compound assignments, and increment/decrement operations; the remaining returned-untouched-and-mutated-by-caller case stays documented as invisible and severity remains Info. |
| LC041 | Hoisted predicate handling | **Partially fixed 2026-07-05.** The fixer now withholds non-inline terminal predicates instead of leaving entity predicates on scalar projections. Hoisted key-predicate analyzer suppression may still miss equivalent single-row-by-key fetches and remains deferred unless new evidence promotes it. The old null-guarded single-property read residual is fixed by the 2026-06-26 pass. |

## Verification Baseline

Package version: **5.6.38**

Base audited commit: master at `17c91cc` (5.6.38 release candidate state). Since the 2026-06-04 baseline (5.5.13): descriptor hygiene (helpLinkUri on all rules, sealed/FixAll architecture tests), repo/CI hardening, the `IncludePathParser` extraction shared by LC006/LC045, **LC045 shipped in 5.6.0** (four pre-ship review-hardening rounds), the **5.6.1 hot-fix** for the LC045 chained-`?.` StackOverflowException that killed csc on 5.6.0, and the July 2026 raw-SQL/fixer hardening through 5.6.38.

Architecture tests enforce the rule quality contract for public package metadata, code-fix provider exports, documentation drift, repository layout, and `samples/LinqContraband.Sample/sample-diagnostics.json` sample expectations.

Current verification (2026-07-16, LC044 nested-member mutation detection after the 5.6.46 release):

- Focused red/green regression confirmed that LC044 missed `user.Address.City = value` after `user` was materialized through `AsNoTracking()`, then passed after mutation recording followed nested property receivers back to the queried local. Codex review exposed the explicit `[NotMapped]` boundary and later confirmed that standalone field-only receivers must remain quiet because EF Core does not map them by convention. GitHub Codex review subsequently confirmed that `Update(user.Address)` and `Entry(user.Address).State = Modified` still reported; member-path-aware state proof now suppresses matching root/owner/nested paths while a sibling operation remains diagnostic. Further Codex passes found that LC044 evaluated only the first mutation, allowing a safely updated `Home` write to hide a later untracked `Work` write; optional post-mutation/foreach operations plus a lexically earlier unreachable save could suppress diagnostics; loop-local transfers could bypass proof; and caught throws could bypass proof while branch targets that still reached it were over-rejected. Suppression now runs independently per mutation path and requires persistence or earlier-save proof on every mutation-to-save path. Review corrected post-mutation `Attach`/`AttachRange` semantics, enumerated all range arguments, distinguished possible narrow catches from definite coverage for base-typed throws, and propagated replacement and rethrow channels independently so a safe rethrow cannot hide an earlier replacement path. Subsequent red/green passes made pre-mutation tracking inside a mandatory first `do` iteration suppress only when no `break`, `continue`, `goto`, return, or caught throw can bypass it; recognised catch-only persistence followed by a compatible rethrow; and treated a nested try whose exact replacement exception always terminates away from saving as unreachable. Later review corrections added exact and implicit try-to-catch/finally save reachability, prevented optional catch-only persistence from suppressing a normal unsafe path, ignored terminating foreach mutations, distinguished constant indexed graph elements without null-sentinel collisions, rejected collective branch proof when condition or a covered branch body can throw before its persistence call, followed unhandled throws out of nested tries, preserved safe collective proof when handlers terminate, and treated property/indexer getters before persistence like calls that can bypass into a fall-through handler. The final review corrections distinguish an implicit transfer consumed before branch persistence from one that can still cross an incompatible inner handler into a fall-through outer catch, treat nullable instance-field reads and required persistence calls, including collective catch-side calls, as possible bypasses without penalising current-instance, conditional-access, or mutually exclusive sibling operations, and accept guaranteed nested blocks, mandatory `do` bodies, nested exhaustive branches, and catch paths that guarantee the same persistence operation. Graph-traversing `Update`/`UpdateRange` coverage is now distinct from exact-entity Modified/Added state coverage and exact-entity detach invalidation, with a paired root-update regression preserving graph-prefix behavior; `Attach`/`AttachRange` suppress only before mutation. Exact/open exception channels, filters, nested handlers, compatible outer catches, and terminating incompatible throws retain their intended precision. Direct, compound, increment, and indexed property mutations use the same receiver-root proof. The LC044 net10.0 slice passes 186 tests and the full net10.0 suite passes all 2,114 tests.

- Focused red/green regression confirmed that `ExecuteSqlRaw($"DELETE FROM {tableName}")` received an `ExecuteSql(...)` fix that would parameterize a table identifier as a value and fail at runtime, then passed after LC034 adopted a likely-value-position guard. Codex review then narrowed automatic fixes to proven core-library scalar parameters in unambiguous single-statement `UPDATE`/`DELETE`/`INSERT` value positions and added guards for formatted/aligned and adjacent holes, char/custom/generic/enum/framework-lookalike value types, provider comments, DML and non-DML batch/multi-statement commands, provider-style backslash-escaped quotes, PostgreSQL dollar-quoted literals, and bracketed, double-quoted, or backtick-delimited identifiers including apostrophes and doubled delimiter escapes. Complete direct scalar `INSERT ... VALUES (...)` rows retain fixes, including multiple rows, while SQL expressions and value-type table/column holes stay manual; mixed sync/async FixAll shares one action identity. Ambiguous shapes remain diagnostic-only. The LC034 net10.0 slice passes 76 tests and the full net10.0 suite passes 1,979 tests.
- Focused red/green regression confirmed that LC045 reported a navigation despite the exact queried context configuring it through an exact top-level `OnModelCreating` `AutoInclude()` chain, then passed after model-level eager-loading paths became context/entity scoped. Guardrails cover constructed generic contexts and keep `IgnoreAutoIncludes()`, fluent, explicit, conditional, or runtime-valued disablement, early-exit, conditional-expression, or deferred configuration, later base/helper boundaries including single- and multi-assignment builder aliases, shadowed or hidden-slot `OnModelCreating` lookalikes, and different context/navigation paths diagnostic. The LC045 net10.0 slice passes 300 tests and the full net10.0 suite passes 1,932 tests.

- Focused red/green regression confirmed that LC009 reported a method as read-only after `user.Profile.DisplayName = name` mutated a nested member of the materialized entity graph and commit was delegated to a helper, then passed after property-reference chains rooted in a materialized entity local counted as write paths. DTO, repointed-local, and indexer-replacement guardrails remain covered. The LC009 net10.0 slice passes 30 tests.
- Focused red/green regression confirmed that LC017's fixer projected only `e.Id` when downstream usage mixed direct foreach access with cast/interface/conversion-based access such as `((IHasName)e).Name` or `((IHasName)e)?.Name`, then passed after conversion-based entity property access made the diagnostic manual-only. The LC017 net10.0 slice passes 43 dedicated tests.
- Focused red/green regressions confirmed that LC041 reported `users.Where(x => x.IsActive).First()` followed by one direct property read without attaching the safe projection fix, and offered a non-compiling scalar-projection fix for `users.First(active)` where `active` was a hoisted entity predicate. Both passed after the fixer bound to the terminal diagnostic invocation and withheld non-inline predicate arguments. The LC041 net10.0 slice passes 23 tests.
- Focused red/green regression confirmed that a hosted service expression-bodied `DbContext` property returning `_factory.CreateDbContext()` reported LC030 like stored context state, then passed after known-fresh computed get-only properties were excluded. Guardrails confirm block-bodied factory getters stay quiet, initialized get-only properties still report, and computed root-service-provider lookups still report rather than being treated as fresh. The LC030 net10.0 slice passes 29 tests.
- Focused red/green regressions confirmed that `ApplyConfigurationsFromAssembly(System.Reflection.Assembly.GetExecutingAssembly())`, `global::System.Reflection.Assembly.GetExecutingAssembly()`, a `using Assembly = System.Reflection.Assembly` alias, a prior local holding that assembly, a readonly context member holding that assembly, an inherited readonly context member, an expression-bodied member alias, and an imported `Assembly.GetExecutingAssembly()` through namespace-level using or a separate-file global using still reported LC011 despite visible current-assembly `IEntityTypeConfiguration<TEntity>` key configuration, then passed after current-assembly proof accepted those expressions. External assembly arguments remain quiet so unrelated configurations do not suppress diagnostics, including local aliases reassigned from the current assembly to an external assembly before the scan call, conditional nested reassignments treated as unresolved, shadowing locals that block member-alias fallback, unqualified `Assembly.GetExecutingAssembly()` calls shadowed by current/parent-namespace types, local/member/foreach/catch/pattern values, or non-System aliases, mutable member aliases, derived mutable members that shadow inherited readonly aliases, and uninvoked local functions that assign the local later. The LC011 net10.0 slice passes 73 tests.
- Focused red/green regression confirmed that LC039 treated separate switch-expression result arms as repeated saves on the same context, then passed after result-arm membership became part of the mutual-exclusion proof. Guardrails confirm repeated saves inside one result arm still report, and saves in `when` guards can still report with a later arm because guard evaluation can fall through. The LC039 net10.0 slice passes 24 tests.
- Focused red/green regression confirmed that LC013 treated a project extension method that materialized before returning `IQueryable<T>` as a transparent query-chain operator, then passed after arbitrary project extensions became origin-resolution boundaries. Guardrails confirm `ToList().AsQueryable()` remains quiet and deferred `AsEnumerable().AsQueryable()` still reports. The LC013 net10.0 slice passes 26 tests.
- Focused red/green regressions confirmed that LC030 skipped static `DbContext` fields and properties before evaluating whether the containing type was long-lived, then passed after static members used the same long-lived-type evidence gate as instance members. Static `DbContext` storage on types with no long-lived proof remains quiet. The LC030 net10.0 slice passes 25 tests.
- Focused red/green regressions confirmed that LC001 missed a helper call inside a nested query lambda when the helper depended only on an outer query range variable, and that the fixer rewrote only the inner correlated query boundary by inserting `.AsEnumerable()` on the nested source. LC001 now reports helpers that depend on the query lambda owning each candidate query invocation and withholds the fixer for diagnostics owned by nested query invocations so those correlated subqueries remain diagnostic-only. The LC001 net10.0 slice passes 42 tests.
- Focused red/green regression confirmed that `users.FirstOrDefault(x => x.Id == x.OtherId)` reported LC023 and offered a non-compiling `users.Find(x.OtherId)` fixer, then passed after the fixer declined key-value expressions that still reference the predicate parameter. The LC023 net10.0 slice passes 25 tests.
- Focused red/green regressions confirmed that the LC012 fixer was withheld for `RemoveRange(query)` when a later `SaveChanges()` appeared in a mutually exclusive `else` branch or on a different freshly-created context local, then passed after the fixer reused the analyzer's branch-exclusivity and fresh-local context proof. Independent review then confirmed cross-context query-source hazards (`query` from the later-save context, from an arbitrary helper that could return it, or from a multi-source composition such as `Concat`); red no-fix regressions now pass after the fixer requires transparent single-source query ownership proof before dismissing the save. The LC012 net10.0 slice passes 35 tests.
- Focused red/green regression confirmed `query.IgnoreQueryFilters(filters)` received the `Remove IgnoreQueryFilters()` code action before the fix and rewrote to `filters.ToList()` instead of `query.ToList()`, then passed after the fixer distinguished extension syntax from static extension wrappers.
- Focused red/green regression confirmed that LC027's fixer inserted `int CustomerId` for a principal with visible Fluent `HasKey(x => x.Code)` string-key configuration, then passed after the fixer read single-property Fluent key metadata before convention fallback. The LC027 net10.0 slice passes 28 tests.
- Focused red/green regression confirmed that LC017's fixer projected only `e.Id` when downstream usage mixed direct foreach access with null-conditional `e?.Name`, then passed after the fixer included supported null-conditional and indexed entity-property access shapes while withholding indexed fixes when the indexed entity escapes. The LC017 net10.0 slice passed 40 dedicated tests at that point.
- Focused red/green regressions confirmed that LC035 reported definitely assigned filtered `if`/`else` locals, all-filtered ternary/switch-expression receivers, reused filtered locals across separate proof paths, overwritten earlier assignments before a later complete filtered `if`/`else`, optional filtered narrowings after that branch base, and stale complete `if`/`else` candidates overwritten by a later filtered complete branch, then passed after the analyzer required every visible branch or arm to be filtered with isolated branch proof state. The LC035 net10.0 slice passes 34 tests.
- Focused red/green regression confirmed that an expression-bodied query method received the LC016 fixer but stayed unchanged, then passed after the fixer converted supported expression-bodied methods/local functions to block bodies and extracted the clock local before the rewritten expression or return. Follow-up regressions confirmed local functions preserve capture timing by extracting inside the converted body, void and async non-generic task methods (including aliased `Task`) use expression statements rather than invalid value returns, expression-bodied FixAll rewrites every non-static query-lambda clock access in the member, expression trivia is preserved, static query lambdas remain diagnostic-only to avoid invalid local captures, and unsupported expression-bodied properties remain diagnostic-only instead of receiving a no-op code action. The LC016 net10.0 slice passes 30 tests.
- Focused red/green regressions confirmed that a misplaced downstream `OrderBy` suppressed the missing-order diagnostic for `Skip` when another pagination operator followed it, in a direct chain, through a simple page alias, and through a sorted alias, then passed after LC015 stopped treating that sort as a protective downstream ordering for the original page boundary. The LC015 net10.0 slice passes 32 tests.
- Focused red/green regressions confirmed that LC045 suppressed a proven missing-`Include` read when the same result or entity escaped, was captured, returned, stored externally, reassigned, or repointed later, and that a navigation write on only one branch, through a stale alias, or on a different extracted entity could suppress the read. The origin-aware forward-flow pass preserves earlier reads, makes uncertainty origin-, prefix-, and binding-generation-specific, intersects definitely-written navigation paths at joins, and handles loop back-edges. Guardrails retain conservative suppression after any uncertain incoming origin and validate straight-line/all-branch same-origin writes, sibling/rebound aliases, conditional and deconstruction stores, composite helper and constructor arguments, direct-index/local identity, reference-navigation locals, prefix-scoped escapes, deferred captures, loop back-edges, exact `ConfigureAwait` wrapping, and rebinding the same origin through different navigation prefixes. All 76 prior behavioural tests remain green alongside 103 new cases, for 179 LC045 behavioural tests and 197 LC045-plus-architecture tests on net10.0.
- Focused red/green regressions confirmed that LC045 missed `DbContext.Set<TEntity>()` query roots, both direct and hoisted through a query local, then passed after the root proof accepted zero-argument `Set<TEntity>()` calls returning `DbSet<TEntity>` while preserving the existing custom-operator bailout. Follow-up guardrails cover `Set<TEntity>()` roots for entity types without a matching DbSet property by adding the proven root entity to the per-analysis entity-type set without mutating the compilation cache. The fixer now inserts `.Include(...)` before materialization on `db.Set<TEntity>()` sources. The LC045 net10.0 slice passes 76 tests.
- Focused red/green regression confirmed that LC045 offered `.Include(...)` on a source widened to `IEnumerable<T>`, producing non-compiling output, then passed after the fixer required the source expression it wraps to be statically `IQueryable<T>`. The LC045 net10.0 slice passes 71 tests.
- Focused red/green regression confirmed that LC044 reported an `AsNoTracking()` entity even when `ctx.Attach(entity)` ran before the property mutation, then passed after same-context re-attaches suppress only when they remain effective through `SaveChanges`. Guardrails cover `DbSet.Update`, `Entry(entity).State = Modified`, optional and guarded pre-attach branches including loop `continue`, reachable explicit detach, and reachable `ChangeTracker.Clear()` shapes that must still report while early-return clear paths stay suppressed. The LC044 net10.0 slice passes 51 tests.
- Focused red/green regression confirmed that LC007 missed `db.Users.Where(...).AsEnumerable().Count()` inside a loop, then passed after deferred `AsEnumerable()` no longer ended EF provenance for the terminal aggregate. A LINQ-to-Objects guard confirms `List<T>.AsEnumerable().Count(...)` inside a loop remains ignored. The LC007 net10.0 slice passes 24 tests.
- Focused red/green regressions confirmed that LC036 missed `DbContext` captures inside `Parallel.For(...)`, `Parallel.Invoke(...)`, and delegate-wrapped callbacks, then passed after the analyzer recognised those `System.Threading.Tasks.Parallel` APIs and descended into callback wrapper expressions. A callback-local context guard confirms `Parallel.For(...)` remains quiet when the work creates its own context. The LC036 net10.0 slice passes 22 tests.
- Focused red/green regressions confirmed that LC010 missed `SaveChanges()` hidden behind a local delegate assigned to a lambda or local-function method group and then invoked inside a loop, then passed after local delegate provenance was followed only when the risky assignment reaches the loop call without definite straight-line reassignment. Review guardrails confirm delegate-wrapped fresh per-iteration contexts stay quiet unless a delegate parameter is reassigned on a path that can reach the save or forwarding call, conditionally assigned delegates that capture a loop-local fresh context still report when they can be carried into later iterations, retry-loop delegate wrappers stay quiet, delegate invocations inside local functions are ordered by executed local-function call sites, uncalled local functions, uncalled lambdas, and nested helper-only calls remain quiet, constructor bodies are analysed, duplicate delegate subscriptions are not cleared by a single matching removal, long-form self-combining assignments such as `save = save + other` preserve the risky save handler, loop-carried assignments report when a later assignment feeds a later iteration, stable branch exits such as `continue` keep mutually exclusive same-iteration calls quiet while loop-variant branch exits still report, opposite-branch assignments report when a loop-variant guard can carry the saved delegate into a later invocation while stable guards stay quiet, wrapper delegate, local-function, and local-invoker callback bodies report when they invoke the current delegate and then assign a save delegate that survives to the next loop call, switch-local `break` statements no longer suppress retry-loop diagnostics for an enclosing per-item loop, mutable conditional initializer and branch guards do not suppress live loop calls, wrapper setup helpers can establish the delegate before a root-level loop, and conditional or mutually exclusive pre-loop delegate reassignments do not hide the risky path. The earlier scoped-receiver regression still confirms that a saved context declared inside the `do` loop body does not receive a non-compiling move-after-loop fixer. The LC010 net10.0 slice passes 133 tests.
- Focused red/green regressions confirmed that LC025 missed entities materialized from `AsNoTrackingWithIdentityResolution()` before `Update(...)`, then passed after the analyzer and fixer treated that EF Core method as a no-tracking directive alongside `AsNoTracking()`. Later red/green regressions confirmed the fixer removed only the branch no-tracking origin when all paths were no-tracking, leaving direct and query-alias fallback paths unchanged; LC025 now reports without a code fix for those multi-origin shapes. The LC025 net10.0 slice passes 33 tests.
- Focused red/green regression confirmed that LC024 missed non-translatable group access inside the `GroupBy(..., (key, group) => ...)` result-selector overload, then passed after the analyzer inspected that result selector with the same projection walker used for `.Select(...)`. The LC024 net10.0 slice passes 26 tests.
- Focused red/green regression confirmed that LC031 missed unbounded EF-backed `ToLookup(...)`, then passed after the analyzer classified `ToLookup` as a collection materializer. The LC031 net10.0 slice passes 23 tests.
- Focused red/green regressions confirmed that LC045 missed inline collection-materializer foreach, direct and hoisted DbSet/IQueryable foreach, nested collection paths, and source-only exact framework element extraction from a materialized collection. The loop route reuses the DbSet/shape-preserving/Include proof and origin-aware CFG; nested iteration binds `Items` as a prefix so `item.Product` reports maximally as `Items.Product`. The fixer uses the diagnostic's exact query-source location for both materializer and direct foreach shapes, revalidates static `IQueryable<T>`, and leaves widened `IEnumerable<T>` sources diagnostic-only. Guardrails keep custom lookalikes, predicate/default-value extractor overloads, repository/parameter roots, and `await foreach` quiet. The focused net10.0 LC045 slice passes 221 tests and the broad LC045-plus-architecture filter passes 239.
- Full local net10.0 suite passes 1,853 tests after the LC045 synchronous-foreach and extraction pass. `RuleCatalogDocGenerator --check` and `SampleDiagnosticsVerifier` for net10.0 pass locally; CI covers net8.0/net9.0 on Ubuntu.
- Local broad multi-target analyzer-verifier tests remain limited on this Mac by the same pre-existing Roslyn test reference issue reproduced on clean `origin/master` (`CS0518`/missing `System.Object`, `DateTime`, and `IQueryable<>` inside verifier compilations). Local arm64 net8.0/net9.0 testhost runs are also unavailable because only arm64 `Microsoft.NETCore.App 10.0.9` is installed.
- Full analyzer test coverage for the release remains delegated to GitHub CI's Ubuntu `dotnet test --no-build --verbosity normal` matrix after PR creation, matching the repository workflow.

Historical baselines: 2026-06-04 rerun verified 919 tests at 5.5.13; 2026-05-29 deep rescan verified 828 tests at 5.4.12 (840d00b); the 2026-05-14 fine-comb re-audit (six parallel slices, scores moved on 30 of 44 rules) established the harsh calibration and the DS=5 anchors (LC011 FP/T/DS, LC030 DS, LC036 DS/Imp) that remain the reference for what a `5` requires.
