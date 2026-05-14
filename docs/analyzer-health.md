# Analyzer Health

Reviewed: 2026-05-14

This is a deliberately harsh health audit for the 44 analyzers in `RuleCatalog`. The catalog currently declares 28 rules with code fixes and 16 manual-only rules with explicit rationale. Scores are 1-5, where `5` means reference-quality and hard to improve, `3` means usable but meaningfully incomplete, and `1` means unreliable or underbuilt.

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

Priority is a planning signal: `High` means the analyzer is important and has meaningful health gaps, `Medium` means useful follow-up work is warranted, and `Low` means no immediate work is needed.

## Scorecard

| Rule | Title | Domain | Severity | Analyzer | False Positives | Fix Strategy | Tests | Docs/Samples | Importance | Priority | Notes |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| LC001 | Local method usage in IQueryable | Query Shape & Translation | Warning | 4 | 3 | 2 | 3 | 3 | 3 | Low | Semantic detection is fine, but the fixer is a thin wrap with no guidance on safer alternatives (compute outside, translatable SQL function); modern EF partial client evaluation lowers urgency. |
| LC002 | Premature query continuation after materialization | Materialization & Projection | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Medium | Strong analyzer/fixer pair with provider safety checks; docs are sparse on `ToList()`/`ToArray()`/`AsEnumerable()` variance and when client boundaries are intentional. |
| LC003 | Prefer Any() over Count() existence checks | Materialization & Projection | Warning | 3 | 4 | 4 | 3 | 2 | 3 | Low | Single-shape binary-comparison detection with a safe fixer; doc is boilerplate (no provider/perf nuance, no scalar-context guidance) and the test file is thin on fixer edge cases. |
| LC004 | IQueryable passed as IEnumerable | Query Shape & Translation | Warning | 4 | 4 | 3 | 4 | 3 | 4 | Low | Solid API-boundary analyzer proving foreach/forwarding/materializing sinks; docs are short on method-body scoping nuance, forwarding chains, and custom-constructor negatives. |
| LC005 | Multiple OrderBy calls | Query Shape & Translation | Warning | 3 | 4 | 4 | 2 | 2 | 3 | Low | Linear chain heuristic with a safe `ThenBy` fixer; only 8 test methods and a 29-line doc — neither covers intentional reset cases, async, or generic-arg variants. |
| LC006 | Multiple collection Includes | Loading & Includes | Warning | 3 | 4 | 3 | 4 | 4 | 4 | Medium | High-impact rule with a sibling-collection precision contract now backed by an explicit two-reference-sibling negative; doc widened (~95 lines) to spell out the `AsSplitQuery()` tradeoff (extra roundtrips, plan cost, snapshot consistency, pagination interaction), legitimate-Cartesian cases, and the rule boundary against LC028 (depth) and LC038 (count). Complex include-chain flow handling stays the residual gap. |
| LC007 | Database execution inside loop | Execution & Async | Warning | 4 | 4 | 4 | 4 | 5 | 5 | Low | Exceptional analyzer and excellent 82-line doc; not a `5` on Analyzer because rarer do-while shapes, complex local-function chains, and conditional execution contexts remain uncovered. |
| LC008 | Synchronous EF method in async context | Execution & Async | Warning | 4 | 4 | 3 | 4 | 3 | 4 | Low | Solid sync/async pair detection; fixer is limited to simple `ToList`→`ToListAsync` rewrites and the 35-line doc has no EF API inventory or "no async equivalent" guidance. |
| LC009 | Missing AsNoTracking in read-only path | Change Tracking & Context Lifetime | Info | 4 | 4 | 2 | 3 | 3 | 3 | Medium | Write-detection logic is decent, but the fixer has only ~2 dedicated tests and the doc never covers when AsNoTracking is unsafe (identity resolution, deferred materialization) — fixer is the primary gap. |
| LC010 | SaveChanges inside loop | Change Tracking & Context Lifetime | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | Mature loop analyzer covering direct/foreach/await-foreach/do/local-function shapes and catch-guarded retry loops; not a `5` on Analyzer because exception-handler and async-iterator-defer cases are still uncovered, matching the LC007 calibration. |
| LC011 | Entity missing primary key | Schema & Modeling | Warning | 4 | 5 | 4 | 5 | 5 | 4 | Low | Reference-quality across FP/T/DS: namespace-validated attributes, mapped key checks, context-specific applied configurations, current-assembly config scanning, inferred owned types, scoped/chained Fluent API, duplicate-safe fixes, 45-test suite, 75-line doc with safe-shape enumeration. Remaining risk is exotic dynamic model construction. |
| LC012 | Use ExecuteDelete instead of RemoveRange | Bulk Operations & Set-Based Writes | Warning | 4 | 4 | 3 | 3 | 3 | 4 | Medium | Conservative analyzer and a safe fixer, but only ~5 fixer tests; doc is 32 lines and never covers the change-tracker bypass vs. pending `SaveChanges` semantics — the unit-of-work timing gap is the primary Medium driver. |
| LC013 | Disposed context query | Change Tracking & Context Lifetime | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | Manual-only lifetime rule with strong single-assignment/conditional/coalesce/switch coverage; rationale for no fixer is sound. Keep expanding alias and ownership boundary tests. |
| LC014 | Avoid string case conversion in queries | Query Shape & Translation | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | Hardened around EF-backed proof, local aliases, manual collation guidance, and `Join`/`GroupJoin` key-selector ownership so in-memory inner/projection selectors stay quiet. |
| LC015 | Missing OrderBy before pagination | Query Shape & Translation | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Medium | High-value reliability rule with strong alias-following; fixer registers on detectable single primary key (`[Key]`, `Id`, `<EntityType>Id`) and now correctly bails on composite-keyed entities to avoid a partial-key `OrderBy`. Doc enumerates the no-fix conditions and offers stable-key guidance (composite keys, time-series tiebreakers, anti-patterns to avoid). |
| LC016 | Avoid DateTime.Now/UtcNow in queries | Query Shape & Translation | Warning | 4 | 4 | 3 | 4 | 2 | 2 | Low | Clock detection and dedup are fine; 32-line doc never covers testability/mocking patterns or modern EF constant-folding, and the impact (cacheability/testability, not correctness) doesn't earn more than Imp=2 against modern EF. |
| LC017 | Whole entity projection | Materialization & Projection | Info | 4 | 4 | 3 | 4 | 5 | 3 | Low | Strong 36-test heuristic with 172-line doc; FS drops to `3` because the fixer rewrites to anonymous types, forcing caller refactoring rather than a safety-preserving change. |
| LC018 | FromSqlRaw with interpolated strings | Raw SQL & Security | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Medium | Security-critical rule with broad call-shape coverage and a now-exhaustive constant-only safe-shape suite (const field, const local, numeric literal, multi-hole, `nameof`) plus boundary lock-ins for mixed const/runtime holes and `static readonly` fields; doc explicitly maps the LC037 upstream-construction boundary. Remaining gap is cross-provider raw-SQL API variants outside `FromSqlRaw` itself. |
| LC019 | Conditional Include expression | Loading & Includes | Warning | 4 | 4 | 3 | 3 | 3 | 4 | Low | Manual-only rule with sound rationale; only ~10 main-file test methods and no filtered-Include or non-EF Include negatives; doc is light on the "split query vs. project earlier" decision tree. |
| LC020 | Untranslatable string comparison overloads | Query Shape & Translation | Warning | 3 | 3 | 4 | 4 | 4 | 3 | Low | Detection of `IQueryable` lambda parameter dependency is sound, but the analyzer is not provider-aware (collation, `EF.Functions` alternatives) and EF 9 `Collate()` is not addressed — both Analyzer and FP drop accordingly. |
| LC021 | IgnoreQueryFilters usage | Raw SQL & Security | Warning | 4 | 4 | 3 | 3 | 4 | 4 | Low | Narrow EF-extension detection is conservative, but the 13-test suite has no `[SuppressMessage]` negative cases and the manual-only rationale lacks "reviewed intentional bypass" examples. |
| LC022 | Nested collection materialization inside projection | Materialization & Projection | Info | 3 | 3 | 3 | 4 | 3 | 2 | Low | EF 9 correlated-collection translation has largely closed the gap this rule originally targeted; analyzer can now flag patterns EF translates correctly, which is why both A/FP and Importance drop. |
| LC023 | Prefer Find/FindAsync for primary key lookups | Materialization & Projection | Info | 4 | 4 | 4 | 4 | 3 | 3 | Low | Helpful cleanup rule with careful key/Async fixer; doc is concise but never enumerates composite-key non-goals or the configured-`HasKey` interaction in depth. |
| LC024 | GroupBy with non-translatable projection | Query Shape & Translation | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Medium | Strong fluent and query-syntax coverage; DS drops to `4` because the 2KB doc doesn't enumerate safe aggregate shapes thoroughly (e.g., `Count`/`Sum` on nested groups), and Imp drops to `4` because EF Core handles many groupings more robustly than when the rule was written. |
| LC025 | AsNoTracking with Update/Remove | Change Tracking & Context Lifetime | Warning | 4 | 4 | 4 | 3 | 4 | 4 | Low | Sound dataflow over nearest origins, foreach paths, and explicit `Entry.State`; edge-case test file is thin on conditional reassignment and `if (...) tracked else untracked` patterns. |
| LC026 | Missing CancellationToken in async call | Execution & Async | Info | 3 | 3 | 4 | 3 | 3 | 3 | Low | Deliberately simple pattern matcher; doc lacks token-naming heuristics and the ambiguous-token boundary noted in earlier sweeps remains a real source of noise. |
| LC027 | Missing explicit foreign key property | Schema & Modeling | Info | 4 | 4 | 4 | 3 | 4 | 3 | Low | Solid modeling rule for teams that want explicit FKs; tests are light on inherited configs and the doc is thin on intentional shadow-FK patterns. |
| LC028 | Deep ThenInclude chain | Loading & Includes | Warning | 3 | 3 | 4 | 3 | 4 | 3 | Low | Configurable-depth heuristic with only 8 test methods; the manual-only rationale is fine but legitimate aggregate-load examples are missing. |
| LC029 | Redundant identity Select | Materialization & Projection | Info | 4 | 4 | 3 | 2 | 4 | 2 | Low | Cosmetic cleanup rule; 4 test methods total and no guidance on intentional materialization-boundary uses of `Select(x => x)`. |
| LC030 | DbContext lifetime mismatch | Change Tracking & Context Lifetime | Info | 4 | 4 | 4 | 4 | 5 | 4 | Low | Strong manual-only lifetime rule with 22-test suite covering BackgroundService/middleware/singleton patterns and an 82-line doc. Severity keeps it out of the urgent stack. |
| LC031 | Unbounded query materialization | Materialization & Projection | Info | 4 | 4 | 3 | 3 | 3 | 3 | Low | Sound chain walker, but FS/T/DS all drop because the rule is advisory-only without a fixer rationale doc, the 15-test suite is light on query-syntax shapes, and intentional full-scan guidance is missing. |
| LC032 | ExecuteUpdate for bulk scalar updates | Bulk Operations & Set-Based Writes | Info | 4 | 4 | 3 | 3 | 3 | 3 | Low | Multi-file analyzer with conservative scalar-assignment detection; manual-only is appropriate but the 14-test suite has no nested/branching loop coverage and the doc lacks fixer-safety examples to back the no-fix stance. |
| LC033 | Use FrozenSet for static membership caches | Materialization & Projection | Info | 4 | 4 | 4 | 4 | 4 | 2 | Low | Healthy niche optimization across 7 files and 18 tests; low user impact makes it a poor near-term investment. |
| LC034 | ExecuteSqlRaw with interpolated strings | Raw SQL & Security | Warning | 4 | 4 | 4 | 4 | 3 | 5 | Low | Strong sync/async raw-SQL detection with named/direct unsafe SQL, lookalike negatives, and quote-sensitive fixer suppression; doc is light on the LC018/LC037 overlap and parameterized-construction guidance. |
| LC035 | Missing Where before bulk execute | Bulk Operations & Set-Based Writes | Info | 4 | 4 | 4 | 3 | 4 | 4 | Medium | High-impact safety smell with strong semantic filter detection; 16-test suite is below peers on filtered-local/reassignment depth, and Imp drops to `4` because transactions and audit logging diminish bare data-loss risk in modern stacks. |
| LC036 | DbContext captured by thread work item | Execution & Async | Warning | 4 | 4 | 4 | 4 | 5 | 5 | Low | High-value thread-safety rule covering lambda, anonymous-method, callback, async-lambda, member capture, factory/scope-safe, materialized-value, and direct local-function shapes; 41-line doc is compact but covers the violation, safer-shape (factory/scope inside callback), intentional-use notes, and the explicit non-goal of arbitrary method-group inspection. |
| LC037 | Constructed raw SQL strings | Raw SQL & Security | Warning | 4 | 4 | 4 | 4 | 3 | 5 | Low | Strong manual security rule with 3-file analyzer detecting concatenation and local resolution; docs still need `StringBuilder`/`string.Format` flow examples and clearer LC018/LC034 boundary text. |
| LC038 | Excessive eager loading | Loading & Includes | Info | 3 | 4 | 4 | 3 | 3 | 2 | Low | Coarse depth-threshold heuristic with only ~6 test methods; modern EF Core split-query support reduces the underlying risk and the 33-line doc has no intentional-load rationale. |
| LC039 | Repeated SaveChanges on same context | Change Tracking & Context Lifetime | Info | 4 | 4 | 4 | 4 | 4 | 4 | Low | Useful reliability smell now guarded for transaction boundaries, repeated saves inside explicit transaction `using` blocks and C# 8+ `using`/`await using` local declarations of an EF Core transaction, mutually exclusive if/else and switch branches, local-function roots, and independent-branch positives; remaining gaps are exception-handler and nested-loop control flow. |
| LC040 | Mixed tracking and no-tracking modes | Change Tracking & Context Lifetime | Info | 3 | 4 | 4 | 3 | 3 | 3 | Low | Basic branch resolution is sound, but neither exception handlers nor nested scopes are handled; 15-test suite has no provider/transaction interaction coverage and the doc is silent on legitimate split-workflow rationale. Importance drops because mixed modes are less critical than explicit data-loss rules. |
| LC041 | Single entity over-fetches one consumed property | Materialization & Projection | Info | 4 | 4 | 3 | 2 | 2 | 2 | Low | Narrow heuristic with 11 test methods; `FirstOrDefault` is excluded from the fixer with no documented rationale, and the 34-line doc lacks multi-property and entity-escape negatives. Modest perf gain, low user visibility. |
| LC042 | Complex query should be tagged | Loading & Includes | Info | 2 | 2 | 4 | 2 | 2 | 2 | Low | Crude operator-count threshold with 4 test methods; no semantic complexity distinction (`Where`+`OrderBy`+`Take` ranks the same as nested `SelectMany`+aggregates). Pure team-policy observability rule. |
| LC043 | Prefer await foreach over buffering async streams | Execution & Async | Info | 4 | 4 | 4 | 3 | 3 | 2 | Low | Narrow async-stream rule with proven `IAsyncEnumerable<T>` source detection; 7 test methods have no CancellationToken or buffer-argument variants and the doc skips concrete examples. Streaming optimization, not a correctness issue. |
| LC044 | AsNoTracking entity mutated then SaveChanges | Change Tracking & Context Lifetime | Warning | 4 | 4 | 4 | 3 | 5 | 5 | Low | Reference-quality manual rule with 76-line doc detailing re-attach gates, earlier-save gates, block reachability, and single-assignment gates; 18-test suite is healthy but lacks foreach-mutation and nested-scope reachability coverage, which is why Tests drops to `3`. |

## Planning Shortlist

The next improvement batch should focus on rules where user impact and health gaps overlap. The fine-comb re-audit promoted three rules to **High** because each combines high importance with a concrete, named gap:

| Priority | Rules | Work |
| --- | --- | --- |
| High | None | The three High targets surfaced by the 2026-05-14 fine-comb re-audit (LC006, LC015, LC018) all shipped precision improvements in 5.4.5–5.4.7. Promote a rule back to High only when concrete false-positive, false-negative, or unsafe-fix evidence surfaces. |
| Medium | LC002, LC006, LC009, LC012, LC015, LC018, LC024, LC035 | LC002 needs provider-variance and intentional-client-boundary docs. LC006's AsSplitQuery tradeoff and rule-boundary docs closed in 5.4.6; complex include-chain flow handling remains. LC009's fixer (FS=2) needs negative/intentional-tracking coverage. LC012 needs unit-of-work timing docs and richer fixer tests. LC015's composite-key partial-fix gap closed in 5.4.7; no-fix Fluent-API and projection guidance is now explicit but Fluent-only key inference remains a non-goal. LC018's constant-only FP gap closed in 5.4.5; cross-provider raw-SQL API variants outside `FromSqlRaw` remain. LC024 needs an enumerated safe-aggregate guide. LC035 needs richer filtered-local/reassignment cases. |
| Low | LC001, LC003, LC004, LC005, LC007, LC008, LC010, LC011, LC013, LC014, LC016, LC017, LC019, LC020, LC021, LC022, LC023, LC025, LC026, LC027, LC028, LC029, LC030, LC031, LC032, LC033, LC034, LC036, LC037, LC038, LC039, LC040, LC041, LC042, LC043, LC044 | Treat as currently acceptable, low-impact tuning, or appropriately harsh-scored where weak. Several of these (LC005, LC016, LC020, LC022, LC031, LC038, LC042) absorbed scoring downgrades on this pass — they remain Low because user impact is also low. |

## Verification Baseline

Package version: 5.4.7

Base audited commit: 62ba410

Architecture tests enforce the rule quality contract for public package metadata, code-fix provider exports, documentation drift, repository layout, and `samples/LinqContraband.Sample/sample-diagnostics.json` sample expectations.

This audit was recalibrated against the current `RuleCatalog`, analyzer/fixer source layout, per-rule test directories, docs, samples, and local verification. The scorecard intentionally does not treat file existence as sufficient evidence of rule quality.

The 2026-05-14 audit was followed by a same-day **fine-comb re-audit** in six parallel slices: each subagent counted actual `[Fact]`/`[Theory]` test methods per rule, read each analyzer source and doc end-to-end, and was explicitly instructed to push borderline 4s down to 3s and to re-evaluate Importance against modern EF Core 9+. The re-audit moved scores on **30 of 44 rules**, with the largest shifts in:

- **Analyzer/FP downgrades** where provider-awareness or semantic depth is thinner than earlier notes claimed: LC003 (A), LC005 (A), LC006 (A), LC007 (A 5→4 — exception-handler/async-iterator-defer gaps prevent a true reference grade), LC018 (FP — constant-only interpolation guards not exhaustively tested), LC020 (A/FP — not provider-aware), LC022 (A/FP — EF 9 correlated collections), LC038 (A), LC040 (A).
- **Fix Strategy downgrades** where the fixer is either advisory or trades one risk for another: LC001 (FS 3→2), LC006 (FS 4→3 — split-query tradeoff), LC009 (FS 3→2 — only ~2 fixer tests), LC012 (FS 4→3), LC017 (FS 4→3 — anonymous-type rewrite forces caller refactor), LC019 (FS), LC021 (FS), LC031 (FS), LC032 (FS).
- **Docs/Samples downgrades** for rules with short docs lacking provider variance, intentional-use patterns, or non-goals: LC002, LC003 (3→2), LC004, LC005 (3→2), LC006 (3→2), LC012, LC016 (3→2), LC019, LC022, LC023, LC024 (5→4), LC026, LC031, LC032, LC034, LC038, LC040, LC041 (3→2), LC042 (3→2), LC043.
- **Test downgrades** based on actual `[Fact]`/`[Theory]` counts: LC002, LC003, LC005 (3→2, 8 methods), LC009, LC012, LC017, LC021, LC025, LC029 stays at 2 (4 methods), LC031, LC035, LC038, LC040, LC041 (3→2), LC043, LC044.
- **Importance downgrades** where modern EF Core or modern patterns reduce real-world impact: LC001, LC002 (5→4), LC003, LC006 (5→4), LC016 (3→2), LC020, LC022 (3→2), LC024 (5→4), LC031, LC032, LC035 (5→4), LC038 (3→2), LC040, LC041 (3→2), LC043 (3→2).

Fourteen rules survived the harsh pass without changes (LC008, LC010, LC011, LC013, LC014, LC015, LC027, LC028, LC029, LC030, LC033, LC036, LC037, LC039). Three of those carry the DS=5 calibration anchors: **LC011** keeps its FP=5/T=5/DS=5 trio (45 tests, 75-line doc, namespace-validated FP); **LC030** keeps its DS=5 (81-line lifetime doc); **LC036** keeps its DS=5/Imp=5 — its 41-line doc is compact but covers the violation, safer-shape via factory/scope, intentional-use notes, and the explicit non-goal of method-group inspection. These three remain the reference for what a `5` requires.

No code, tests, or samples changed between the original 2026-05-14 sweep and the fine-comb re-audit, so the verification baseline below still applies as written. Git verified: `git log 43b7c1a..HEAD -- src/ tests/ samples/` returns empty.

Current local verification:

- `dotnet run --project tools/RuleCatalogDocGenerator/RuleCatalogDocGenerator.csproj -- --check` reported `docs/rule-catalog.md` is up to date.
- `dotnet run --project tools/SampleDiagnosticsVerifier/SampleDiagnosticsVerifier.csproj --configuration Release -- --configuration Release --frameworks net10.0` passed for 43 diagnostic paths.
- `dotnet test tests/LinqContraband.Tests/LinqContraband.Tests.csproj --framework net10.0 --no-restore --filter FullyQualifiedName~LC010_SaveChangesInLoop` passed with 29 tests.
- `dotnet test tests/LinqContraband.Tests/LinqContraband.Tests.csproj --framework net10.0 --no-restore --filter FullyQualifiedName~LC012_OptimizeRemoveRange` passed with 17 tests.
- `dotnet test tests/LinqContraband.Tests/LinqContraband.Tests.csproj --framework net10.0 --no-restore --filter FullyQualifiedName~LC018_AvoidFromSqlRawWithInterpolation` passed with 30 tests.
- `dotnet test tests/LinqContraband.Tests/LinqContraband.Tests.csproj --framework net10.0 --no-restore --filter FullyQualifiedName~LC021_AvoidIgnoreQueryFilters` passed with 13 tests.
- `dotnet test tests/LinqContraband.Tests/LinqContraband.Tests.csproj --framework net10.0 --no-restore --filter FullyQualifiedName~LC034_AvoidExecuteSqlRawWithInterpolation` passed with 28 tests.
- `dotnet test tests/LinqContraband.Tests/LinqContraband.Tests.csproj --framework net10.0 --no-restore --filter FullyQualifiedName~LC035_MissingWhereBeforeExecuteDeleteUpdate` passed with 16 tests.
- `dotnet test tests/LinqContraband.Tests/LinqContraband.Tests.csproj --framework net10.0 --no-restore --filter FullyQualifiedName~LC039_NestedSaveChanges` passed with 19 tests.
- `dotnet test tests/LinqContraband.Tests/LinqContraband.Tests.csproj --framework net10.0 --no-restore --filter FullyQualifiedName~LC040_MixedTrackingAndNoTracking` passed with 15 tests.
- `dotnet test LinqContraband.sln --framework net10.0 --no-restore` passed with 809 tests.
- `dotnet pack src/LinqContraband/LinqContraband.csproj --configuration Release --output /tmp/linqcontraband-5.4.7` produced `LinqContraband.5.4.7.nupkg`.
- `git diff --check` passed.
- `dotnet --list-runtimes` shows only .NET 10 runtimes in this local environment, so full multi-target verification remains blocked by missing .NET 8 and .NET 9 runtimes.
