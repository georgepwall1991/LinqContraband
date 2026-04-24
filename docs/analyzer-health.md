# Analyzer Health

Reviewed: 2026-04-24

This is an actionable health audit for the 44 analyzers in `RuleCatalog`. The current catalog declares 30 rules with code fixes and 14 rules as manual-only with explicit rationale. Scores are 1-5, where `5` is excellent, `3` is usable with gaps, and `1` needs urgent attention.

## Rubric

| Metric | Meaning |
| --- | --- |
| Analyzer | Accuracy and semantic depth of the analyzer implementation. |
| False Positives | Conservatism around ambiguous sources, opt-outs, edge cases, and intentional usage. |
| Fix Strategy | Quality of the fixer, or quality of the manual-only rationale when no safe fixer exists. |
| Tests | Strength of analyzer, fixer, negative, edge-case, and cross-analyzer tests. |
| Docs/Samples | Clarity and consistency of rule docs, sample coverage, metadata, and documented non-goals. |
| Importance | User-facing usefulness based on frequency, severity, security/reliability/performance impact, and actionability. |

Priority is a planning signal: `High` means the analyzer is important and has meaningful health gaps, `Medium` means useful follow-up work is warranted, and `Low` means no immediate work is needed.

## Scorecard

| Rule | Title | Domain | Severity | Analyzer | False Positives | Fix Strategy | Tests | Docs/Samples | Importance | Priority | Notes |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| LC001 | Local method usage in IQueryable | Query Shape & Translation | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | Healthy core translation rule; keep expanding negative tests as new LINQ patterns appear. |
| LC002 | Premature query continuation after materialization | Materialization & Projection | Warning | 5 | 4 | 5 | 5 | 5 | 5 | Low | Strong analyzer/fixer/test depth; good model for complex performance rules. |
| LC003 | Prefer Any() over Count() existence checks | Materialization & Projection | Warning | 5 | 5 | 5 | 5 | 4 | 4 | Low | Mature rule with broad edge-case and fixer coverage. |
| LC004 | IQueryable passed as IEnumerable | Query Shape & Translation | Warning | 5 | 4 | 4 | 4 | 5 | 4 | Low | Strong semantic implementation; keep watching for intentional API-boundary cases. |
| LC005 | Multiple OrderBy calls | Query Shape & Translation | Warning | 4 | 4 | 3 | 3 | 4 | 3 | Medium | Analyzer is straightforward; add explicit fixer tests to raise confidence. |
| LC006 | Multiple collection Includes | Loading & Includes | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | Useful high-impact EF performance rule; fixer to `AsSplitQuery` is appropriately conservative. |
| LC007 | Database execution inside loop | Execution & Async | Warning | 5 | 5 | 5 | 5 | 5 | 5 | Low | Excellent rule with clear ignored cases and conservative explicit-loading fixer behavior. |
| LC008 | Synchronous EF method in async context | Execution & Async | Warning | 5 | 4 | 4 | 5 | 4 | 4 | Low | Strong coverage including edge cases; keep async API mapping current. |
| LC009 | Missing AsNoTracking in read-only path | Change Tracking & Context Lifetime | Info | 4 | 4 | 4 | 4 | 4 | 3 | Low | Good write-detection coverage; low severity makes this a tuning rule, not a top priority. |
| LC010 | SaveChanges inside loop | Change Tracking & Context Lifetime | Warning | 4 | 4 | 4 | 4 | 4 | 5 | Low | High-value write-side N+1 rule; current fixer guidance appears sufficient. |
| LC011 | Entity missing primary key | Schema & Modeling | Warning | 5 | 5 | 4 | 5 | 4 | 4 | Low | Strong convention/configuration handling and broad edge-case tests. |
| LC012 | Use ExecuteDelete instead of RemoveRange | Bulk Operations & Set-Based Writes | Warning | 3 | 3 | 3 | 3 | 4 | 4 | Medium | Useful but semantically delicate; add more negative tests around cascades, tracked entities, and provider/version limits. |
| LC013 | Disposed context query | Change Tracking & Context Lifetime | Warning | 5 | 5 | 5 | 5 | 4 | 5 | Low | Excellent manual-only candidate with strong lifetime edge-case coverage. |
| LC014 | Avoid string case conversion in queries | Query Shape & Translation | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | Healthy rule; future work is provider/collation nuance rather than core correctness. |
| LC015 | Missing OrderBy before pagination | Query Shape & Translation | Warning | 4 | 4 | 4 | 4 | 3 | 5 | Medium | High-value reliability rule; docs contain implementation-note residue and should be tightened. |
| LC016 | Avoid DateTime.Now/UtcNow in queries | Query Shape & Translation | Warning | 5 | 5 | 4 | 5 | 4 | 3 | Low | Well-tested rule; importance is moderate because impact is usually cacheability/testability. |
| LC017 | Whole entity projection | Materialization & Projection | Info | 5 | 5 | 5 | 5 | 4 | 4 | Low | Very strong edge-case coverage for a heuristic Info rule. |
| LC018 | FromSqlRaw with interpolated strings | Raw SQL & Security | Warning | 4 | 4 | 3 | 3 | 4 | 5 | High | Security-important; add explicit fixer tests and more constructed-SQL negative/positive cases. |
| LC019 | Conditional Include expression | Loading & Includes | Warning | 4 | 4 | 5 | 3 | 4 | 5 | Medium | Manual-only rationale is sound; add more Include/ThenInclude shape and non-EF negative tests. |
| LC020 | Untranslatable string comparison overloads | Query Shape & Translation | Warning | 4 | 4 | 3 | 3 | 4 | 4 | Medium | Add explicit fixer tests and provider translation edge cases. |
| LC021 | IgnoreQueryFilters usage | Raw SQL & Security | Warning | 4 | 3 | 4 | 3 | 4 | 4 | Medium | Intentional bypasses can be valid; consider suppression/allow-list guidance and more negative tests. |
| LC022 | ToList/ToArray inside Select projection | Materialization & Projection | Warning | 4 | 4 | 3 | 4 | 4 | 4 | Medium | Analyzer coverage is decent; add dedicated fixer tests if fixer behavior is meant to be supported. |
| LC023 | Prefer Find/FindAsync for primary key lookups | Materialization & Projection | Info | 3 | 3 | 3 | 3 | 4 | 3 | Medium | Useful but schema-sensitive; add fixer tests and more composite-key/provider edge cases. |
| LC024 | GroupBy with non-translatable projection | Query Shape & Translation | Warning | 4 | 4 | 5 | 3 | 4 | 5 | Medium | Manual-only is correct; expand translation-boundary tests because runtime failure impact is high. |
| LC025 | AsNoTracking with Update/Remove | Change Tracking & Context Lifetime | Warning | 4 | 4 | 3 | 3 | 4 | 4 | Medium | Reliability value is clear; add fixer tests or narrow catalog confidence if fixer coverage is intentionally absent. |
| LC026 | Missing CancellationToken in async call | Execution & Async | Info | 4 | 4 | 3 | 4 | 4 | 3 | Medium | Good analyzer scope; add fixer tests across method parameters, locals, and default token cases. |
| LC027 | Missing explicit foreign key property | Schema & Modeling | Info | 4 | 4 | 3 | 4 | 4 | 3 | Medium | Design guidance is useful; add fixer tests and more model-configuration negative cases. |
| LC028 | Deep ThenInclude chain | Loading & Includes | Warning | 3 | 3 | 5 | 3 | 4 | 3 | Medium | Heuristic rule; manual-only is right, but thresholds and false-positive guidance need more tests. |
| LC029 | Redundant identity Select | Materialization & Projection | Info | 5 | 5 | 5 | 3 | 4 | 2 | Low | Simple, safe cleanup rule; low importance but healthy implementation. |
| LC030 | DbContext lifetime mismatch | Change Tracking & Context Lifetime | Info | 3 | 3 | 3 | 3 | 4 | 4 | High | Important architecture smell; fixer surface is complex and needs broader DI/lifetime tests. |
| LC031 | Unbounded query materialization | Materialization & Projection | Info | 3 | 3 | 5 | 3 | 4 | 4 | Medium | Valuable but heuristic; expand safe opt-out, Take/Where ordering, and intentional full-scan coverage. |
| LC032 | ExecuteUpdate for bulk scalar updates | Bulk Operations & Set-Based Writes | Info | 4 | 4 | 5 | 4 | 4 | 4 | Low | Conservative manual-only rule with solid analysis split; revisit fixer only after more semantics are modeled. |
| LC033 | Use FrozenSet for static membership caches | Materialization & Projection | Info | 5 | 5 | 5 | 4 | 4 | 2 | Low | Strong implementation for a niche optimization; not a planning priority. |
| LC034 | ExecuteSqlRaw with interpolated strings | Raw SQL & Security | Warning | 5 | 5 | 5 | 5 | 5 | 5 | Low | Hardened to reference quality with parameter-aware SQL argument resolution, narrow safe fixer behavior, raw-parameter guardrails, LC037 boundary coverage, and aligned docs/sample guidance. |
| LC035 | Missing Where before bulk execute | Bulk Operations & Set-Based Writes | Info | 4 | 4 | 5 | 3 | 4 | 5 | Medium | Safety impact is high; add more ExecuteDelete/ExecuteUpdate chain and intentional full-table operation tests. |
| LC036 | DbContext captured by thread work item | Execution & Async | Warning | 4 | 4 | 5 | 3 | 4 | 5 | Medium | Important safety rule; add coverage for more task/parallel APIs and safe factory/scope patterns. |
| LC037 | Constructed raw SQL strings | Raw SQL & Security | Warning | 5 | 5 | 5 | 5 | 4 | 5 | Low | Strong manual-only security rule with broad string-construction coverage. |
| LC038 | Excessive eager loading | Loading & Includes | Info | 3 | 3 | 5 | 3 | 4 | 3 | Medium | Heuristic rule; add threshold documentation and more examples of legitimate deep loads. |
| LC039 | Repeated SaveChanges on same context | Change Tracking & Context Lifetime | Info | 4 | 4 | 5 | 3 | 4 | 4 | Medium | Useful reliability smell; add more transaction, branch, and intentional boundary tests. |
| LC040 | Mixed tracking and no-tracking modes | Change Tracking & Context Lifetime | Info | 4 | 4 | 5 | 3 | 4 | 4 | Medium | Manual-only is appropriate; add broader context-resolution and split-workflow tests. |
| LC041 | Single entity over-fetches one consumed property | Materialization & Projection | Info | 4 | 4 | 3 | 3 | 4 | 3 | Medium | Useful but subtle; add explicit fixer tests and more multi-use/side-effect negative cases. |
| LC042 | Complex query should be tagged | Loading & Includes | Info | 3 | 3 | 5 | 3 | 4 | 2 | Low | Team-policy rule; low importance unless observability standards require tags. |
| LC043 | Prefer await foreach over buffering async streams | Execution & Async | Info | 3 | 3 | 3 | 3 | 4 | 3 | Medium | Add fixer tests and more cases around multiple enumeration, ordering, and list reuse. |
| LC044 | AsNoTracking entity mutated then SaveChanges | Change Tracking & Context Lifetime | Warning | 5 | 5 | 5 | 5 | 5 | 5 | Low | Excellent high-impact data-loss rule with strong edge-case coverage and clear manual guidance. |

## Planning Shortlist

The next improvement batch should focus on rules that combine high importance with weaker health:

| Priority | Rules | Work |
| --- | --- | --- |
| High | LC018 | Add explicit security fixer tests and bring query raw-SQL coverage up to LC034 parity. |
| High | LC030 | Broaden DI lifetime and fixer tests; validate safe patterns using factories/scopes. |
| Medium | LC012, LC023, LC025, LC026, LC027, LC041, LC043 | Add missing or thin fixer test coverage before relying on these fixes heavily. |
| Medium | LC019, LC024, LC028, LC031, LC035, LC036, LC038, LC039, LC040 | Expand negative tests and documented intentional-use guidance for manual-only heuristics. |
| Low | LC002, LC003, LC007, LC013, LC017, LC034, LC037, LC044 | Treat as reference-quality examples for future analyzer work. |

## Verification Baseline

`dotnet restore LinqContraband.sln` completed successfully.

`dotnet test LinqContraband.sln --no-restore --framework net10.0` currently builds and runs successfully with 522 passing tests.

`dotnet test LinqContraband.sln --no-restore` currently builds and runs the `net10.0` test target successfully with 522 passing tests. The `net8.0` and `net9.0` test hosts abort in this local environment because arm64 .NET 8 and .NET 9 runtimes are not installed; no test failures were reported before those host aborts.
