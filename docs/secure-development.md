---
title: Secure Development for LinqContraband
description: Secure-design principles and practical mitigations for analyzer, scanner, and release maintenance.
---

# Secure development for LinqContraband

Use this guide when reviewing changes and completing the OpenSSF developer-knowledge declarations. Reading or
publishing it does not establish that a human developer understands it; the maintainer must confirm that separately.
The [official criterion details](https://www.bestpractices.dev/en/criteria/0?details=true#know_secure_design)
include all ten principles below.

## Design principles in this project

| Principle | Application |
| --- | --- |
| Economy of mechanism | Keep rule logic and release paths small enough to inspect; reuse proven shared helpers. |
| Fail-safe defaults | Reject unknown scanner options and invalid release metadata; start workflow tokens read-only. |
| Complete mediation | Check the release tag/version and required CI gates on every applicable operation. |
| Open design | Keep security logic reviewable in source; protect credentials rather than depending on hidden implementation. |
| Separation of privilege | Require PR and CI gates; add independent human approval once an eligible reviewer exists. Use MFA for maintainer accounts. |
| Least privilege | Grant token capabilities only to the job that needs them; use short-lived OIDC publishing credentials. |
| Least common mechanism | Use isolated temporary locations and minimize shared state between builds and scans. |
| Psychological acceptability | Provide clear option errors, release checks and a discoverable private reporting route. |
| Limited attack surface | The analyzer has no database service or web endpoint; enable external services only for a defined purpose. |
| Allowlist input validation | Restrict rule IDs and severity names, and validate counts and release identifiers before use. |

## Relevant errors and mitigations

| Risk | Mitigation to preserve in reviews |
| --- | --- |
| Shell or command injection | Pass scanner arguments through `ProcessStartInfo.ArgumentList` with `UseShellExecute=false`; quote shell variables and keep external text out of shell source. |
| Untrusted project execution | A scanner build runs project-defined MSBuild logic. Use trusted repositories or an isolated execution environment; argument escaping does not sandbox a build. |
| HTML/script injection | Encode project-controlled values in HTML output. `HtmlReport` uses `WebUtility.HtmlEncode`; do not replace encoding with raw interpolation. |
| SQL injection in analyzed applications | Detect unsafe SQL construction, while avoiding promises that a diagnostic certifies application safety. Verify fixes against real supported EF Core versions. |
| Missing authorization | Keep explicit owner/member/collaborator and `@claude` trigger checks on write-capable automation. Required checks must remain enforced for administrators. |
| Exposed credentials | Keep credentials out of source, logs and artifacts. Use secret stores/OIDC, secret scanning and push protection; rotate and revoke a confirmed exposed credential. |
| Dependency substitution or tampering | Review upstream action SHAs, lock-file content hashes, test-package SHA-512 and downloaded-tool SHA-256 changes. Do not disable integrity checks to make a build pass. |
| Unsafe parsing or denial of service | Exercise arbitrary inputs with FsCheck, keep bounds and cancellation where relevant, and investigate crashes, hangs or excessive work. |
| Unsafe memory operations | Prefer managed APIs; review any future `unsafe`/native interop boundary explicitly and add suitable dynamic analysis before shipping it. |

## Release and incident practice

Run static analysis and the complete automated suite, including variable-input properties, before a major release.
Resolve confirmed exploitable medium-or-higher findings promptly; public security defects need affected/fixed version
information and assigned CVE/GHSA identifiers in release notes. Follow [SECURITY.md](https://github.com/georgepwall1991/LinqContraband/blob/master/SECURITY.md)
for private reporting and response targets.

AI review supplements these checks. It does not count as another human understanding a change, so the independent
review-history finding remains until eligible humans review future contributions.
