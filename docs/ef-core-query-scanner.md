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
where, down to the line of code. Your project files are not changed.

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

## Fail a Build on Findings

`--fail-on` turns the scan into a CI gate, and the filters decide what counts. This fails on any warning outside tests
and migrations, and ignores the advisory unbounded-query rule:

```bash
dnx LinqContraband.Scan -- --fail-on warning --exclude 'tests/**' --exclude Migrations --skip-rules LC031
```

Filtered findings are left out of the SARIF report too, and the summary says how many were left out.

## Upload the Report to GitHub Code Scanning

The SARIF report lists file paths relative to the git repository, so GitHub can place each finding on its line. This
workflow shows findings as pull request annotations and in the repository's Security tab:

```yaml
name: linqcontraband-scan

on:
  pull_request:
  push:
    branches: [main]

permissions:
  contents: read
  security-events: write

jobs:
  scan:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dnx --yes LinqContraband.Scan -- --sarif linqcontraband.sarif
      - uses: github/codeql-action/upload-sarif@v3
        with:
          sarif_file: linqcontraband.sarif
          category: linqcontraband
```

`--yes` skips the prompt that asks before downloading the tool. Add a `dotnet-version` line for each SDK your
solution needs. Code scanning is free on public repositories; private repositories need GitHub Code Security.

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
  so one project's findings cannot keep the projects that depend on it from being built and scanned.

The build writes to the usual `bin/` and `obj/` folders. No package is restored from NuGet for the analyzer, and the
injected file is deleted when the scan ends.

Your repository's `.editorconfig` rule settings still apply, so a rule you have turned off stays off. A rule set to
`error` there still fails that project's build, and the projects that depend on it are then not scanned. The scanner
names those rules when it happens; set them to `warning` to scan everything.

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
