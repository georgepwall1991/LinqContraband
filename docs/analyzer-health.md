# Analyzer Health

Reviewed: 2026-04-27

This is a deliberately harsh health audit for the 44 analyzers in `RuleCatalog`. The catalog currently declares 29 rules with code fixes and 15 manual-only rules with explicit rationale. Scores are 1-5, where `5` means reference-quality and hard to improve, `3` means usable but meaningfully incomplete, and `1` means unreliable or underbuilt.

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

- A `5` is rare. For a fixable rule it requires broad analyzer coverage plus dedicated fixer coverage and strong negative tests.
- A manual-only rule can score well on Fix Strategy, but it still needs explicit intentional-use guidance and negative tests.
- Info rules are scored by product value, not implementation effort. Some are healthy but still low importance.
- Docs/Samples score existence and quality. Every rule has a doc and sample, but thin docs do not get a free `5`.

Priority is a planning signal: `High` means the analyzer is important and has meaningful health gaps, `Medium` means useful follow-up work is warranted, and `Low` means no immediate work is needed.

## Scorecard

| Rule | Title | Domain | Severity | Analyzer | False Positives | Fix Strategy | Tests | Docs/Samples | Importance | Priority | Notes |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| LC001 | Local method usage in IQueryable | Query Shape & Translation | Warning | 4 | 3 | 3 | 3 | 3 | 4 | Low | Useful semantic rule, but still needs more non-query, provider, and expression-shape negatives before it is truly hardened. |
| LC002 | Premature query continuation after materialization | Materialization & Projection | Warning | 4 | 4 | 4 | 5 | 4 | 5 | Low | One of the stronger rules; remaining risk is mostly long-tail query-shape precision. |
| LC003 | Prefer Any() over Count() existence checks | Materialization & Projection | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | Mature simple rewrite rule, but docs are fairly thin for provider/perf nuance. |
| LC004 | IQueryable passed as IEnumerable | Query Shape & Translation | Warning | 4 | 3 | 3 | 3 | 3 | 4 | Medium | Valuable API-boundary rule; intentional abstractions and library boundary cases need more negative coverage. |
| LC005 | Multiple OrderBy calls | Query Shape & Translation | Warning | 4 | 4 | 4 | 3 | 3 | 3 | Low | Safe, focused rule; more explicit edge/fixer tests would raise confidence. |
| LC006 | Multiple collection Includes | Loading & Includes | Warning | 4 | 4 | 4 | 4 | 3 | 5 | Low | High-impact EF performance rule with a conservative `AsSplitQuery` fixer; docs should explain when split queries are not a free win. |
| LC007 | Database execution inside loop | Execution & Async | Warning | 5 | 4 | 4 | 4 | 5 | 5 | Low | Strong analyzer and docs; remaining gaps are around rarer loop/dataflow shapes and fixer boundaries. |
| LC008 | Synchronous EF method in async context | Execution & Async | Warning | 4 | 4 | 3 | 4 | 3 | 4 | Low | Good coverage for common sync-over-async shapes; fixer confidence depends on keeping API mappings current. |
| LC009 | Missing AsNoTracking in read-only path | Change Tracking & Context Lifetime | Info | 4 | 4 | 3 | 4 | 3 | 3 | Low | Good write-detection coverage for a tuning rule; fixer/docs need clearer identity-resolution and intentional tracking guidance. |
| LC010 | SaveChanges inside loop | Change Tracking & Context Lifetime | Warning | 3 | 3 | 3 | 3 | 3 | 5 | Medium | Very important rule, but current health is more basic than the impact justifies; add branch, loop, async, and unit-of-work negatives. |
| LC011 | Entity missing primary key | Schema & Modeling | Warning | 4 | 5 | 4 | 5 | 5 | 4 | Low | Hardened around namespace-validated attributes, mapped key checks, context-specific applied configurations, current-assembly config scanning, inferred owned types, scoped/chained builder Fluent API, and duplicate-safe fixes; remaining risk is mostly exotic/dynamic EF model construction. |
| LC012 | Use ExecuteDelete instead of RemoveRange | Bulk Operations & Set-Based Writes | Warning | 3 | 3 | 3 | 2 | 3 | 4 | Medium | Useful but risky rewrite space; needs more source-shape, provider/version, and fixer safety tests. |
| LC013 | Disposed context query | Change Tracking & Context Lifetime | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | Strong manual-only reliability rule; keep expanding lifetime-alias and ownership boundary coverage. |
| LC014 | Avoid string case conversion in queries | Query Shape & Translation | Warning | 3 | 3 | 3 | 3 | 3 | 4 | Medium | Correct basic smell, but provider/collation nuance is under-modeled and should be documented more clearly. |
| LC015 | Missing OrderBy before pagination | Query Shape & Translation | Warning | 4 | 4 | 3 | 3 | 4 | 5 | Medium | High-value reliability rule; fixer remains inherently advisory because choosing a stable key is domain-specific. |
| LC016 | Avoid DateTime.Now/UtcNow in queries | Query Shape & Translation | Warning | 4 | 4 | 3 | 4 | 3 | 3 | Low | Good focused detection; importance is moderate because impact is usually cacheability/testability rather than correctness. |
| LC017 | Whole entity projection | Materialization & Projection | Info | 4 | 4 | 4 | 5 | 5 | 3 | Low | Very healthy for a heuristic Info rule; keep no-fix/escape boundaries tight. |
| LC018 | FromSqlRaw with interpolated strings | Raw SQL & Security | Warning | 4 | 4 | 4 | 3 | 4 | 5 | Medium | Security-critical and mostly sound, but one test file and limited dedicated fixer coverage leave avoidable risk. |
| LC019 | Conditional Include expression | Loading & Includes | Warning | 4 | 4 | 4 | 3 | 4 | 4 | Low | Good manual-only rule; would benefit from more filtered Include and non-EF Include edge cases. |
| LC020 | Untranslatable string comparison overloads | Query Shape & Translation | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | Hardened around Queryable expression-lambda proof, direct/nested query-parameter-dependent receivers, captured local/constant negatives, and semantically bound fixer argument removal. |
| LC021 | IgnoreQueryFilters usage | Raw SQL & Security | Warning | 3 | 2 | 2 | 2 | 3 | 4 | High | Intentional bypasses are common enough that the rule needs suppression/allow-list guidance and stronger negative tests. |
| LC022 | Nested collection materialization inside projection | Materialization & Projection | Info | 4 | 4 | 3 | 4 | 4 | 3 | Low | Advisory projection-shape rule after production hardening; severity and wording now reflect modern EF Core correlated collection support. |
| LC023 | Prefer Find/FindAsync for primary key lookups | Materialization & Projection | Info | 3 | 3 | 3 | 2 | 4 | 3 | Medium | Helpful cleanup rule, but key-shape/model-awareness and async fixer cases remain thin. |
| LC024 | GroupBy with non-translatable projection | Query Shape & Translation | Warning | 4 | 4 | 4 | 3 | 4 | 5 | Medium | High-impact manual rule; add more provider/LINQ-to-Objects boundaries and nested projection negatives. |
| LC025 | AsNoTracking with Update/Remove | Change Tracking & Context Lifetime | Warning | 4 | 4 | 3 | 3 | 4 | 4 | Medium | Good core data-loss guardrail; fixer and alias/order coverage should be broadened. |
| LC026 | Missing CancellationToken in async call | Execution & Async | Info | 3 | 3 | 4 | 3 | 4 | 3 | Low | Fix strategy is comparatively strong; analyzer value and ambiguous-token boundaries keep priority low. |
| LC027 | Missing explicit foreign key property | Schema & Modeling | Info | 4 | 4 | 4 | 3 | 4 | 3 | Low | Solid modeling rule for teams that want explicit FKs; low severity limits planning urgency. |
| LC028 | Deep ThenInclude chain | Loading & Includes | Warning | 3 | 3 | 4 | 3 | 4 | 3 | Low | Reasonable manual-only heuristic; threshold and legitimate aggregate-load examples need ongoing attention. |
| LC029 | Redundant identity Select | Materialization & Projection | Info | 4 | 4 | 3 | 2 | 4 | 2 | Low | Simple, safe cleanup rule; not worth prioritizing unless tests are being rounded out opportunistically. |
| LC030 | DbContext lifetime mismatch | Change Tracking & Context Lifetime | Info | 4 | 4 | 4 | 4 | 5 | 4 | Low | Strong manual-only lifetime rule with useful docs; severity keeps it out of the urgent stack. |
| LC031 | Unbounded query materialization | Materialization & Projection | Info | 2 | 2 | 4 | 2 | 3 | 4 | High | Valuable idea, but source tracking, intentional full scans, existing filters, config opt-outs, and tests are too shallow. |
| LC032 | ExecuteUpdate for bulk scalar updates | Bulk Operations & Set-Based Writes | Info | 4 | 4 | 4 | 3 | 4 | 4 | Low | Conservative manual-only design is appropriate; revisit only after higher-risk bulk rules are healthier. |
| LC033 | Use FrozenSet for static membership caches | Materialization & Projection | Info | 4 | 4 | 4 | 4 | 4 | 2 | Low | Healthy niche optimization; low user impact makes it a poor near-term investment. |
| LC034 | ExecuteSqlRaw with interpolated strings | Raw SQL & Security | Warning | 4 | 4 | 4 | 3 | 4 | 5 | Medium | Security-critical and broadly covered, but dedicated fixer/negative test organization should be stronger. |
| LC035 | Missing Where before bulk execute | Bulk Operations & Set-Based Writes | Info | 3 | 3 | 4 | 3 | 3 | 5 | Medium | High-impact safety smell despite Info severity; needs richer intentional bulk-operation guidance and negatives. |
| LC036 | DbContext captured by thread work item | Execution & Async | Warning | 4 | 4 | 4 | 3 | 4 | 5 | Medium | High-value thread-safety rule; add more callback, closure, scope-factory, and materialized-value boundary tests. |
| LC037 | Constructed raw SQL strings | Raw SQL & Security | Warning | 4 | 4 | 4 | 4 | 3 | 5 | Low | Strong manual security rule; docs should better explain parameterized construction patterns and LC018/LC034 overlap. |
| LC038 | Excessive eager loading | Loading & Includes | Info | 3 | 2 | 4 | 2 | 3 | 3 | Medium | Configurable heuristic, but false-positive boundaries and intentional deep-load documentation are thin. |
| LC039 | Repeated SaveChanges on same context | Change Tracking & Context Lifetime | Info | 3 | 3 | 4 | 2 | 2 | 4 | High | Useful reliability smell, but branch, transaction, local-function, and unit-of-work cases need much more coverage. |
| LC040 | Mixed tracking and no-tracking modes | Change Tracking & Context Lifetime | Info | 3 | 3 | 4 | 2 | 2 | 4 | High | Important data-behavior smell; context-resolution and legitimate split-workflow tests/docs are underbuilt. |
| LC041 | Single entity over-fetches one consumed property | Materialization & Projection | Info | 4 | 4 | 3 | 3 | 3 | 3 | Low | Solid heuristic projection rule; more fixer and entity-escape coverage would help but is not urgent. |
| LC042 | Complex query should be tagged | Loading & Includes | Info | 2 | 2 | 4 | 2 | 3 | 2 | Low | Team-policy rule with low general importance; weak coverage is acceptable unless observability becomes a product goal. |
| LC043 | Prefer await foreach over buffering async streams | Execution & Async | Info | 3 | 3 | 3 | 2 | 3 | 3 | Medium | Useful async guidance, but fixer and reused-buffer boundaries need more tests before trusting broad application. |
| LC044 | AsNoTracking entity mutated then SaveChanges | Change Tracking & Context Lifetime | Warning | 4 | 4 | 4 | 4 | 5 | 5 | Low | Strong high-impact manual rule with clear data-loss framing; keep adding mutation and escape edge cases over time. |

## Planning Shortlist

The next improvement batch should focus on rules where user impact and health gaps overlap:

| Priority | Rules | Work |
| --- | --- | --- |
| High | LC021 | Harden Warning rules with weak fixer/test confidence. Add dedicated fixer tests, intentional-use guidance, and tighter diagnostic placement. |
| High | LC031, LC039, LC040 | Build out high-value heuristic rules. Add source/context aliasing, branch/transaction/workflow negatives, opt-out guidance, and richer docs before raising scores. |
| Medium | LC010, LC012, LC014, LC015, LC018, LC023, LC024, LC025, LC034, LC035, LC036, LC038, LC043 | Improve targeted tests and docs, especially around safe/unsafe fixer boundaries and intentional-use cases. |
| Low | LC002, LC003, LC005, LC006, LC007, LC008, LC009, LC011, LC013, LC016, LC017, LC019, LC020, LC022, LC026, LC027, LC028, LC029, LC030, LC032, LC033, LC037, LC041, LC042, LC044 | Treat as currently acceptable, reference examples, or low-impact tuning rules. |

## Verification Baseline

Architecture tests enforce the rule quality contract for public package metadata, code-fix provider exports, documentation drift, repository layout, and `samples/LinqContraband.Sample/sample-diagnostics.json` sample expectations.

This audit was recalibrated against the current `RuleCatalog`, analyzer/fixer source layout, per-rule test directories, docs, samples, and local verification. The scorecard intentionally does not treat file existence as sufficient evidence of rule quality.

Current local verification:

- `dotnet run --project tools/RuleCatalogDocGenerator/RuleCatalogDocGenerator.csproj -- --check` reported `docs/rule-catalog.md` is up to date.
- `dotnet run --project tools/SampleDiagnosticsVerifier/SampleDiagnosticsVerifier.csproj --configuration Release -- --configuration Release --frameworks net10.0` passed for 43 diagnostic paths.
- `dotnet test tests/LinqContraband.Tests/LinqContraband.Tests.csproj --framework net10.0 --filter FullyQualifiedName~LC011` passed with 45 tests.
- `dotnet test LinqContraband.sln --framework net10.0` passed with 671 tests.
- `git diff --check` passed.
- `dotnet --list-runtimes` shows only .NET 10 runtimes in this local environment, so full multi-target verification remains blocked by missing .NET 8 and .NET 9 runtimes.
