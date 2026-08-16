---
layout: default
title: Real-World Corpus Validation
description: How LinqContraband proves zero false positives and bounded analyzer execution time on pinned real-world EF Core codebases before every release.
permalink: /corpus-validation.html
---

# Real-World Corpus Validation

LinqContraband ships 46 analyzers that make semantic claims about Entity Framework Core queries. Unit tests prove each rule against hand-written fixtures — but a rule that is correct on fixtures can still crash, hang, or misfire on code nobody in this repository wrote. The corpus validation engine closes that gap.

Every rule also runs against a **pinned corpus of real open-source EF Core applications**. A diagnostic appears on the corpus only if a human triaged it as either a genuine issue in that codebase or a false positive that must be fixed before the next release. The same run measures per-analyzer execution time on those real compilations and fails if any rule blows its budget.

## What the engine enforces

| Gate | Meaning |
| --- | --- |
| Zero untriaged diagnostics | Any `LC###` diagnostic on the corpus must be classified in `tools/CorpusValidator/corpus-triage.json` as `true-positive` or `false-positive`. A false-positive verdict is a release blocker: fix the rule or document why it stands. |
| Zero stale triage entries | A triage entry whose diagnostic no longer reproduces fails validation, so the triage file always reflects reality at the pinned commits. |
| Zero analyzer exceptions | An analyzer crash (`AD0000`-class) on real code fails the run immediately. |
| Per-analyzer time budgets | Each analyzer must run on each corpus project within `max(baseline × 1.35, 2s)` and under a hard per-rule cap (default 2 minutes), so a quadratic blowup on a large real file can never ship silently. |

## The corpus

The corpus lives in [`tools/CorpusValidator/corpus-manifest.json`](https://github.com/georgepwall1991/LinqContraband/blob/master/tools/CorpusValidator/corpus-manifest.json) and is pinned to exact commit hashes:

| Repository | What it exercises |
| --- | --- |
| [eShopOnWeb](https://github.com/dotnet-architecture/eShopOnWeb) | Microsoft's reference ASP.NET Core + EF Core application; multi-project solution, catalog seeding, specification patterns. |
| [CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture) | Popular Clean Architecture template; Identity, migrations, seeding, repository-style access across four projects. |

Adding a repository is a manifest entry: a name, a `.git` URL, a 40-character commit hash, and the `.csproj` files to analyze. The first `validate` run against a new repository produces the untriaged list to classify.

## Running locally

```bash
dotnet run --project tools/CorpusValidator/CorpusValidator.csproj -- prepare   # clone / pin corpus
dotnet run --project tools/CorpusValidator/CorpusValidator.csproj -- validate  # triage gate
dotnet run --project tools/CorpusValidator/CorpusValidator.csproj -- perf      # time-budget gate
```

Use `perf --update-baseline` after an intentional performance change to record a new committed baseline, and `--corpus-root` to keep checkouts outside the repository. Corpus checkouts are never committed; only the manifest, triage decisions, and perf baseline are.

## Continuous integration

The [Corpus Validation workflow](https://github.com/georgepwall1991/LinqContraband/actions/workflows/corpus.yml) runs the full engine weekly and on demand: prepare pinned checkouts, validate the triage contract, and check execution-time budgets. Pull requests are covered by the 2,600+ test suite; the weekly run is the scheduled real-world regression net, and it can be dispatched manually on any branch before a release.

## Findings so far

The first corpus run (2026-08-16, package 5.8.0) reported five diagnostics across both repositories — all five triaged as **true positives** — and **zero false positives**. It also caught a live analyzer defect that all unit tests missed: LC004 crashed with `ArgumentException: SyntaxTree is not part of the compilation` whenever a forwarding callee lived in a referenced project, because multi-project solutions cannot be expressed in single-compilation unit fixtures. The same guard class was applied to LC045 and LC046, which walk declaring-syntax references in the same way.

That is the pattern this engine exists to industrialize: real code finds real defects that fixtures cannot reach.
