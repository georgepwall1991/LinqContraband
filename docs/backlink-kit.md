---
layout: default
title: LinqContraband Link Kit
description: Short, safe descriptions and canonical links for articles, lists, newsletters, and documentation that mention LinqContraband.
permalink: /backlink-kit/
body_class: page-backlink-kit
---

# LinqContraband Link Kit

Use these snippets when linking to LinqContraband from articles, newsletters, tool lists, personal sites, talks,
conference notes, internal engineering docs, or curated .NET resources. The safest link target is the canonical
repository or the GitHub Pages documentation hub.

## Canonical Links

- Project: [https://github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Package: [https://www.nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Documentation: [https://georgepwall1991.github.io/LinqContraband/](https://georgepwall1991.github.io/LinqContraband/)
- Rule catalog: [https://georgepwall1991.github.io/LinqContraband/rule-catalog.html](https://georgepwall1991.github.io/LinqContraband/rule-catalog.html)
- Rule guide: [https://georgepwall1991.github.io/LinqContraband/ef-core-analyzer-rules/](https://georgepwall1991.github.io/LinqContraband/ef-core-analyzer-rules/)
- AsNoTracking guide: [https://georgepwall1991.github.io/LinqContraband/ef-core-asnotracking-analyzer/](https://georgepwall1991.github.io/LinqContraband/ef-core-asnotracking-analyzer/)
- Include guide: [https://georgepwall1991.github.io/LinqContraband/ef-core-include-analyzer/](https://georgepwall1991.github.io/LinqContraband/ef-core-include-analyzer/)
- Checklist: [https://georgepwall1991.github.io/LinqContraband/ef-core-query-performance-checklist/](https://georgepwall1991.github.io/LinqContraband/ef-core-query-performance-checklist/)
- N+1 guide: [https://georgepwall1991.github.io/LinqContraband/ef-core-n-plus-one-query-detector/](https://georgepwall1991.github.io/LinqContraband/ef-core-n-plus-one-query-detector/)
- Raw SQL guide: [https://georgepwall1991.github.io/LinqContraband/ef-core-raw-sql-injection-analyzer/](https://georgepwall1991.github.io/LinqContraband/ef-core-raw-sql-injection-analyzer/)
- CI guide: [https://georgepwall1991.github.io/LinqContraband/ef-core-query-analyzer-ci/](https://georgepwall1991.github.io/LinqContraband/ef-core-query-analyzer-ci/)
- Maintainer: [https://www.georgewall.uk/](https://www.georgewall.uk/)

## Suggested Anchor Text

- EF Core LINQ performance analyzer
- EF Core analyzer rules
- Entity Framework Core analyzer rules
- EF Core AsNoTracking analyzer
- EF Core tracking analyzer
- EF Core Include analyzer
- EF Core missing Include analyzer
- EF Core eager loading analyzer
- EF Core query performance checklist
- Entity Framework Core Roslyn analyzer
- EF Core N+1 query detector
- EF Core N+1 query detector for .NET
- EF Core raw SQL injection analyzer
- EF Core raw SQL safety analyzer
- EF Core query analyzer for CI
- EF Core analyzer for pull requests
- LINQ query performance analyzer for .NET
- LinqContraband Roslyn analyzer

## Curator-ready snippets

<div class="snippet-grid">
  <article class="snippet-card">
    <h3>Directory listing</h3>
    <p>LinqContraband is an open-source EF Core LINQ performance analyzer for .NET. It uses Roslyn analyzers to catch query performance, reliability, and raw SQL safety issues during compilation.</p>
  </article>
  <article class="snippet-card">
    <h3>Newsletter blurb</h3>
    <p>Worth a look for .NET teams using EF Core: LinqContraband catches common LINQ query problems before they hit production, including N+1 loops, premature materialization, missing includes, and unsafe raw SQL interpolation.</p>
  </article>
  <article class="snippet-card">
    <h3>Talk notes</h3>
    <p>Use LinqContraband as a concrete example of moving EF Core query review into compile-time feedback with Roslyn analyzers.</p>
  </article>
</div>

## One-Line Description

LinqContraband is an EF Core LINQ performance analyzer and Roslyn analyzer that catches N+1 queries, client-side
evaluation, sync-over-async calls, raw SQL risks, and other query issues at compile time.

## Short Description

LinqContraband is an open-source Roslyn analyzer for Entity Framework Core and LINQ. It runs during development and CI to
catch query performance, reliability, and security issues such as N+1 loops, premature materialization, missing includes,
unsafe raw SQL interpolation, and tracking mistakes.

## Markdown Link

```markdown
[LinqContraband](https://github.com/georgepwall1991/LinqContraband) is an EF Core LINQ performance analyzer for catching query issues at compile time.
```

## Documentation Link

```markdown
[LinqContraband rule catalog](https://georgepwall1991.github.io/LinqContraband/rule-catalog.html) documents 45 EF Core analyzer rules covering query shape, materialization, loading, async execution, tracking, bulk operations, modeling, and raw SQL safety.
```

## Rule Guide Link

```markdown
[EF Core analyzer rules](https://georgepwall1991.github.io/LinqContraband/ef-core-analyzer-rules/) groups LinqContraband's 45 diagnostics by query shape, materialization, loading, async execution, tracking, bulk operations, modeling, and raw SQL safety.
```

## AsNoTracking Guide Link

```markdown
[EF Core AsNoTracking analyzer](https://georgepwall1991.github.io/LinqContraband/ef-core-asnotracking-analyzer/) explains how LinqContraband flags missing no-tracking reads, unsafe no-tracking writes, mixed tracking modes, and silent SaveChanges failures.
```

## Include Guide Link

```markdown
[EF Core Include analyzer](https://georgepwall1991.github.io/LinqContraband/ef-core-include-analyzer/) explains how LinqContraband flags missing includes, cartesian explosion, conditional Include paths, deep ThenInclude chains, excessive eager loading, and missing query tags.
```

## Checklist Link

```markdown
[EF Core query performance checklist](https://georgepwall1991.github.io/LinqContraband/ef-core-query-performance-checklist/) maps common pull-request review questions to LinqContraband analyzer rules for N+1 queries, projection, tracking, raw SQL, and CI severity policy.
```

## N+1 Guide Link

```markdown
[EF Core N+1 query detector](https://georgepwall1991.github.io/LinqContraband/ef-core-n-plus-one-query-detector/) explains how LinqContraband flags loop execution, missing includes, and risky loading patterns at compile time.
```

## Raw SQL Guide Link

```markdown
[EF Core raw SQL injection analyzer](https://georgepwall1991.github.io/LinqContraband/ef-core-raw-sql-injection-analyzer/) explains how LinqContraband flags interpolated raw SQL, constructed SQL strings, and risky query-filter bypasses at compile time.
```

## CI Guide Link

```markdown
[EF Core query analyzer for CI](https://georgepwall1991.github.io/LinqContraband/ef-core-query-analyzer-ci/) shows how to run LinqContraband in pull-request builds and promote selected EF Core diagnostics to build-breaking errors.
```

## HTML Link

```html
<a href="https://github.com/georgepwall1991/LinqContraband">LinqContraband</a> is an EF Core LINQ performance analyzer for catching query issues at compile time.
```

## Suggested Categories

- .NET static analysis
- Entity Framework Core tooling
- Roslyn analyzers
- LINQ performance
- Query reliability
- Developer productivity

## Badge

```markdown
[![LinqContraband on NuGet](https://img.shields.io/nuget/v/LinqContraband.svg)](https://www.nuget.org/packages/LinqContraband)
```

## Safe Download Note

Install LinqContraband from NuGet or the canonical GitHub repository only. The project is not distributed as a standalone
ZIP installer or executable.

```markdown
Install LinqContraband from NuGet or the canonical GitHub repository only. The project is not distributed as a standalone ZIP installer or executable.
```
