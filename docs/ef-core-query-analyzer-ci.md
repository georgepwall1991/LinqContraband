---
layout: default
title: EF Core Query Analyzer for CI
description: Use LinqContraband as an EF Core query analyzer in CI so pull requests can catch N+1 queries, raw SQL risks, missing includes, and tracking mistakes before merge.
permalink: /ef-core-query-analyzer-ci/
body_class: page-ci-guide
---

# EF Core Query Analyzer for CI

LinqContraband is an EF Core query analyzer for CI pipelines, pull requests, and local builds. It ships as a NuGet
analyzer package, so ordinary `dotnet build` output can surface risky LINQ and Entity Framework Core query patterns
before they merge.

For the review questions that this CI gate can automate, use the
[EF Core query performance checklist](/LinqContraband/ef-core-query-performance-checklist/).

Install the official NuGet package in the project that contains your EF Core code:

```bash
dotnet add package LinqContraband
```

## Why Run EF Core Analysis in CI

EF Core query mistakes often depend on production-sized data: N+1 loops, unbounded materialization, missing includes,
raw SQL interpolation, and tracking-mode surprises may not appear in small test databases. A compile-time analyzer gives
reviewers the same signal on every pull request, without adding a runtime dependency to the application.

Use the CI gate to make the most dangerous diagnostics visible first, then raise selected rules to errors once the team
has triaged existing warnings.

## Minimal GitHub Actions Check

Run the analyzer through the normal .NET build step. Match the SDK version to your application:

```yaml
name: ef-core-query-analysis

on:
  pull_request:

jobs:
  query-analysis:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet restore
      - run: dotnet build --configuration Release --no-restore
```

No custom runner, service container, or database connection is required for LinqContraband diagnostics. The analyzer
reviews C# source during compilation.

## Make Selected Rules Block Pull Requests

The quickest gate is a preset. Add one property to the project file, or to `Directory.Build.props` to cover a whole
solution:

```xml
<PropertyGroup>
  <LinqContrabandPreset>security</LinqContrabandPreset>
</PropertyGroup>
```

| Preset | Effect |
| --- | --- |
| `security` | LC018, LC034, and LC037 (SQL injection through raw SQL) become build errors, together with EF Core's own EF1002 and EF1003, which LC018 and LC034 defer to. |
| `critical` | `security` plus LC013, LC019, LC036, LC044, LC046, LC047, LC048, LC054, LC055, LC056, and LC058 (runtime failures and silent data loss) become build errors. |
| `strict` | Every warning rule becomes an error and every advisory rule becomes a warning. |
| `essentials` | Advisory (Info) rules are turned off, so only the warnings remain. |

Presets combine with `;` (`security;essentials`). They ship as global analyzer configs inside the package, so any
`dotnet_diagnostic.LCxxx.severity` line in your `.editorconfig` still overrides them. An unknown preset name produces
an `LCPRESET` build warning.

For finer control, set severities in `.editorconfig`. Start by promoting high-confidence security and query-cost rules:

```ini
[*.cs]

# Repeated database calls and missing includes
dotnet_diagnostic.LC007.severity = error
dotnet_diagnostic.LC045.severity = warning

# Raw SQL and filter bypass risks
dotnet_diagnostic.LC018.severity = error
dotnet_diagnostic.LC034.severity = error
dotnet_diagnostic.LC037.severity = error
dotnet_diagnostic.LC021.severity = warning
```

This keeps CI focused: dangerous raw SQL construction can fail a build immediately, while broader reliability guidance
can remain a warning until the team agrees on the policy.

## Rules Worth Surfacing Early

| Rule | CI value |
| --- | --- |
| [LC007: database execution inside loop](/LinqContraband/LC007_NPlusOneLooper.html) | Catches N+1 patterns that small test datasets hide. |
| [LC045: missing include](/LinqContraband/LC045_MissingInclude.html) | Flags navigation access after materialization when the query did not include the navigation. |
| [LC018: interpolated FromSqlRaw and SqlQueryRaw](/LinqContraband/LC018_AvoidFromSqlRawWithInterpolation.html) | Blocks SQL injection-prone raw query strings. |
| [LC034: interpolated ExecuteSqlRaw](/LinqContraband/LC034_AvoidExecuteSqlRawWithInterpolation.html) | Blocks SQL injection-prone raw command strings. |
| [LC037: constructed raw SQL strings](/LinqContraband/LC037_RawSqlStringConstruction.html) | Finds SQL strings built before they reach a raw EF Core API. |
| [LC021: IgnoreQueryFilters](/LinqContraband/LC021_AvoidIgnoreQueryFilters.html) | Makes tenant, soft-delete, and security-filter bypasses auditable in review. |

## CI Rollout Pattern

1. Add LinqContraband and run CI with default severities.
2. Fix obvious high-confidence warnings in touched code.
3. Promote a small rule set to `error` in `.editorconfig`.
4. Use narrow pragmas or `SuppressMessage` only for reviewed exceptions with a concrete justification.
5. Expand the enforced rule set once the warning backlog is under control.

For deeper rule detail, browse the [full rule catalog](/LinqContraband/rule-catalog.html), the
[EF Core N+1 query detector guide](/LinqContraband/ef-core-n-plus-one-query-detector/), the
[EF Core CancellationToken analyzer guide](/LinqContraband/ef-core-cancellation-token-analyzer/), the
[EF Core projection analyzer guide](/LinqContraband/ef-core-projection-analyzer/), and the
[EF Core raw SQL injection analyzer guide](/LinqContraband/ef-core-raw-sql-injection-analyzer/).

## Upload Findings to GitHub Code Scanning

To see findings as pull request annotations and in the repository's Security tab, run the
[LinqContraband scanner](/LinqContraband/ef-core-query-scanner/) and upload its SARIF report. The scanner page has the
workflow.

## Official Links

- Canonical repository: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Official NuGet package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Documentation hub: [georgepwall1991.github.io/LinqContraband](https://georgepwall1991.github.io/LinqContraband/)
- Safe install guidance: [Official LinqContraband downloads and authenticity](/LinqContraband/security-and-authenticity/)
