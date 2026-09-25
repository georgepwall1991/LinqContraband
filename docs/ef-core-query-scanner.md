---
layout: default
title: Scan a .NET Solution for EF Core Query Problems
description: Run dnx LinqContraband.Scan to find N+1 queries, client-side evaluation and raw SQL risks in any .NET solution without adding a package, then upload SARIF to GitHub.
permalink: /ef-core-query-scanner/
body_class: page-scanner-guide
---

# Scan a .NET Solution for EF Core Query Problems

`LinqContraband.Scan` shows what LinqContraband would report on your code before you add it to a project. One command
builds your solution with the analyzers injected and prints a ranked report: which rules fired, how often, and
where, down to the line of code. Your project files are not changed unless you pass `--fix`, which applies the
rules' code fixes for you.

```bash
dnx LinqContraband.Scan
```

`dnx` runs a .NET tool straight from NuGet without installing it, and needs the .NET 10 SDK. On the .NET 8 or 9 SDK,
install the tool once instead:

```bash
dotnet tool install -g LinqContraband.Scan
linqcontraband-scan
```

## What You Get

Run in a directory that holds a solution or project, or pass one explicitly:

```bash
dnx LinqContraband.Scan -- src/MyApp.sln
```

```text
LinqContraband found 38 problems (9 rules, 14 files).

  Rule   Severity Count  Title
  LC015  Warning      6  Deterministic Pagination: OrderBy required before Skip/Take
  LC007  Warning      5  N+1 Problem: Database execution inside loop
  LC002  Warning      4  Premature query continuation after materialization
  LC008  Warning      2  Sync-over-Async: Synchronous EF Core method in Async context
  LC018  Warning      1  Avoid FromSqlRaw with interpolated strings
  LC031  Info        11  Unbounded Query Materialization
  LC009  Info         6  Performance: Missing AsNoTracking() in Read-Only path
  ...

Findings:

  LC015  Deterministic Pagination: OrderBy required before Skip/Take
    src/Orders/OrderService.cs:88:14
      The method 'Skip' is called on an unordered IQueryable. Call 'OrderBy' or 'OrderByDescending' first to ensure deterministic results.
      88 | var page = await db.Orders.Skip(offset).Take(size).ToListAsync();
    src/Reports/SalesReport.cs:41:22
      The method 'Take' is called on an unordered IQueryable. Call 'OrderBy' or 'OrderByDescending' first to ensure deterministic results.
      41 | return db.Sales.Where(s => s.Year == year).Take(50).ToList();
    ...and 4 more in the SARIF report.
  ...

Most affected files:
      7  src/Orders/OrderService.cs
      5  src/Reports/SalesReport.cs
  ...

What each rule means and how to fix it:
  LC015  https://georgepwall1991.github.io/LinqContraband/LC015_MissingOrderBy.html
  ...

SARIF report: linqcontraband.sarif
```

Warnings come first, then advisory (Info) findings, each sorted by count. Under "Findings", each rule lists its first
three findings with the message and the line of code; `--findings all` lists every one, and `--findings 0` leaves the
section out. A multi-targeted project compiles each file
once per target framework, so the report counts a finding once however many frameworks report it. Code suppressed
with `#pragma warning disable` or `[SuppressMessage]` is left out, as it is in a normal build.

The report also includes EF Core's own EF1002 and EF1003 warnings about SQL injection through raw SQL methods. On EF
Core 8 and later, LC018 and LC034 leave those calls to EF Core instead of reporting them twice, so the scan shows EF
Core's diagnostic for them.

## Options

| Option | Meaning |
| --- | --- |
| `<path>` | Solution (`.sln` or `.slnx`), project, or directory. Defaults to the current directory. |
| `-o`, `--sarif <file>` | Where to write the SARIF 2.1.0 report. Defaults to `linqcontraband.sarif`. |
| `-c`, `--configuration <name>` | Build configuration, passed to `dotnet build`. |
| `-f`, `--framework <tfm>` | Build one target framework only, which is faster for multi-targeted projects. |
| `--no-restore` | Skip the implicit restore. |
| `--rules <ids>` | Report only these rules, comma-separated, such as `LC007,LC008`. Repeatable. |
| `--skip-rules <ids>` | Leave these rules out, comma-separated. Repeatable. |
| `--exclude <glob>` | Leave out findings in files matching a glob, such as `tests/**` or `**/Migrations/**`. A glob without `/` matches a file or folder name anywhere, so `Migrations` and `*.Designer.cs` work alone. Repeatable. |
| `--fail-on <level>` | Exit with code 1 when a reported finding is at least this severe: `error`, `warning` or `info`. Defaults to `none`. |
| `--baseline <file>` | A SARIF report from an earlier scan. Only findings it does not have are listed and count for `--fail-on`. |
| `--fix` | Apply the rules' code fixes to your source files first, then report what is left. `--rules`, `--skip-rules` and `--exclude` limit what is fixed. |
| `--html <file>` | Also write the report as one self-contained HTML page. |
| `--summary <file>` | Also write the report as Markdown. |
| `--no-github` | In GitHub Actions, skip the job summary and pull request annotations. |
| `--findings <n>` | How many findings to list under each rule, with the line of code. `all` lists every finding, `0` none. Defaults to 3. |
| `--top <n>` | How many files to list under "Most affected files". Defaults to 10. |
| `-v`, `--verbose` | Show the full `dotnet build` output. |
| `--version` | Show the version. The analyzer the tool runs has the same version. |
| `-h`, `--help` | Show usage. |

Put the scanner's arguments after `--` when using `dnx`, so that `dnx` does not read options such as `-v` or
`--version` as its own.

The exit code is 0 when the scan completes, whatever it finds, unless `--fail-on` is set: then it is 1 when a finding
at that severity or higher is left after the filters. It is 2 for a usage error and 3 when the build fails (the report
still covers the projects that compiled before the failure).

## Apply the Fixes

Most rules have a code fix, the same one the IDE offers from the light bulb. `--fix` applies them all in one go,
then scans again and reports what is left for a person to decide:

```bash
dnx LinqContraband.Scan -- --fix
```

```text
Fixing /src/Shop/Shop.sln with LinqContraband 5.12.0 (dotnet format)...
Applied fixes to 4 files. Review them with 'git diff' before you commit:
  src/Catalog.API/Apis/CatalogApi.cs
  src/Catalog.API/Infrastructure/CatalogContextSeed.cs
  ...
```

This edits your source files, so start from a clean working tree, read the diff, and run your tests before you commit.
A fixer only rewrites the cases it can change safely: it adds `AsNoTracking()` only when the entities stay inside
the method, for example. Those it leaves alone stay in the report. To fix one rule at a time, which keeps each diff
easy to review, pass `--rules`:

```bash
dnx LinqContraband.Scan -- --fix --rules LC009
```

`--skip-rules` keeps a rule's fixes out, and `--exclude` keeps files as they were, such as `--exclude Migrations`.
The fixes run through `dotnet format analyzers`, which comes with the .NET SDK, so rules turned off in `.editorconfig`
are not fixed either.

## Share the Report

`--html report.html` writes the whole report as one HTML page that needs nothing else to open: the rules that fired,
then every finding with its message and the code around it, the finding's line highlighted. A search box and severity
toggles narrow the list, and it follows the reader's light or dark setting. Attach it to a ticket, send it to the team,
or keep it as a CI artifact.

```bash
dnx LinqContraband.Scan -- --html linqcontraband.html
```

## Fail a Build on Findings

`--fail-on` turns the scan into a CI gate, and the filters decide what counts. This fails on any warning outside tests
and migrations, and ignores the advisory unbounded-query rule:

```bash
dnx LinqContraband.Scan -- --fail-on warning --exclude 'tests/**' --exclude Migrations --skip-rules LC031
```

Filtered findings are left out of the SARIF report too, and the summary says how many were left out.

## Fail Only on New Findings

An existing codebase usually has findings nobody will fix this week. A baseline lets the scan fail only on findings
that are new, so the gate can go in today. Scan once and commit the report:

```bash
dnx LinqContraband.Scan -- --sarif linqcontraband.baseline.sarif
git add linqcontraband.baseline.sarif
```

Then scan against it in CI:

```bash
dnx LinqContraband.Scan -- --baseline linqcontraband.baseline.sarif --fail-on warning
```

The report lists only findings the baseline does not have, and says how many known ones it left out. Each finding in
the SARIF report carries a fingerprint built from its rule, its file and its line of code (ignoring whitespace), so a
finding still matches after code above it moves it to another line or it is re-indented. When you fix a known
finding it drops out on its own; refresh the baseline file whenever you want the known list to shrink. The new SARIF
report keeps every finding and marks each one `new` or `unchanged` in `baselineState`.

## Run It in GitHub Actions

The repository is also a GitHub Action. It installs the scanner that matches the action's version, scans, and in the
job summary lists every rule that fired with each finding linked to its line. The most severe findings are also
annotated on the pull request's changed lines.

```yaml
name: linqcontraband

on:
  pull_request:
  push:
    branches: [main]

permissions:
  contents: read
  security-events: write   # only for upload-sarif

jobs:
  scan:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - uses: georgepwall1991/LinqContraband@v5.12.0
        with:
          fail-on: warning
          baseline: linqcontraband.baseline.sarif   # optional: fail only on new findings
          exclude: |
            tests/**
            Migrations
          upload-sarif: true
```

| Input | Meaning |
| --- | --- |
| `path` | Solution, project or directory to scan. Defaults to the repository root. |
| `fail-on` | Fail the job when a finding is at least this severe: `error`, `warning`, `info` or `none` (the default). |
| `baseline` | A committed SARIF report from an earlier scan; only findings it does not have count. |
| `exclude` | Globs of files to leave out, one per line. |
| `args` | Any other scanner options, such as `-c Release --skip-rules LC031`. |
| `upload-sarif` | `true` to upload the report to GitHub code scanning, which shows findings in the Security tab. Defaults to `false`. |
| `category` | The code scanning category for the upload. Defaults to `linqcontraband`. |
| `version` | The scanner version. Defaults to the version in the action's tag. |

Add a `dotnet-version` line for each SDK your solution needs. The report is uploaded before a `fail-on` failure fails
the job, so code scanning still gets it. Code scanning is free on public repositories; private repositories need GitHub
Code Security.

The scanner itself notices GitHub Actions, so a plain `dnx LinqContraband.Scan` step gets the same job summary and
annotations; `--no-github` turns them off. `--summary report.md` writes the Markdown report to a file anywhere, for a
pull request comment or a wiki page.

The SARIF report lists file paths relative to the git repository, so GitHub can place each finding on its line. Each
rule carries its description, a link to its page and `efcore` plus category tags, which code scanning shows on the
alert.

## How It Works

The scanner runs `dotnet build` on your solution with an extra MSBuild file injected through
`CustomAfterMicrosoftCommonTargets`. That file adds the LinqContraband analyzer the tool ships with (replacing any
version the project already references, so nothing is reported twice) and writes a SARIF log for each compilation.
For the scan it also:

- turns analyzers on, since some repositories switch them off for local builds with `RunAnalyzersDuringBuild`;
- turns off the .NET SDK's own code-quality and code-style analyzers, which the report does not use and which slow the
  build;
- rebuilds every project, because a project that is already up to date is not compiled, and its analyzers would not
  run;
- stops treating warnings as errors and ignores `LinqContrabandPreset`, whose presets make some findings build errors,
  so one project's findings cannot keep the projects that depend on it from being built and scanned;
- resets each LinqContraband rule (and EF1002/EF1003) to its default severity in a global analyzer config, so a
  severity your `.editorconfig` sets for all analyzers or a whole category, such as
  `dotnet_analyzer_diagnostic.severity = error`, does not turn the findings into build errors either. The scan reports
  those findings at the rule's default severity, and a rule turned off only that way is still scanned. Compiler
  errors (`CSxxxx`) still fail the build and the scan.

The build writes to the usual `bin/` and `obj/` folders. No package is restored from NuGet for the analyzer, and the
injected files are deleted when the scan ends.

Your repository's settings for a rule by its id (`dotnet_diagnostic.LC009.severity = ...` in `.editorconfig` or a
global config) still apply, so a rule you have turned off stays off. A rule set to `error` by its id still fails that
project's build, and the projects that depend on it are then not scanned. The scanner
names those rules when it happens; set them to `warning` to scan everything.

`--fix` runs `dotnet format analyzers` with the same file injected through the `CustomAfterMicrosoftCommonTargets`
environment variable, which MSBuild reads as a property, and limits it to the rules that have a fix. Then it runs the
scan above.

## Keep the Checks

The scan is a snapshot. To see these diagnostics as you type, get code fixes in the editor, and keep them in every
build, add the analyzer package:

```bash
dotnet add package LinqContraband
```

Then see the [CI guide](/LinqContraband/ef-core-query-analyzer-ci/) for severity presets that fail a build on the
rules that matter most, and the [rule catalog](/LinqContraband/rule-catalog.html) for what each rule catches.

## Official Links

- Scanner package: [nuget.org/packages/LinqContraband.Scan](https://www.nuget.org/packages/LinqContraband.Scan)
- Analyzer package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Source: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Safe install guidance: [Official LinqContraband downloads and authenticity](/LinqContraband/security-and-authenticity/)
