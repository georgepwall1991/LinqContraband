---
layout: default
title: Official LinqContraband Downloads and Authenticity
description: Verify the official LinqContraband repository and NuGet package before installing or linking to the project.
permalink: /security-and-authenticity/
---

# Official LinqContraband Downloads and Authenticity

Use these links when installing, reviewing, or linking to LinqContraband:

- Canonical source repository: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Official NuGet package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Official scanner tool: [nuget.org/packages/LinqContraband.Scan](https://www.nuget.org/packages/LinqContraband.Scan)
- Maintainer: [George Wall](https://www.georgewall.uk/)

LinqContraband is a NuGet analyzer package for .NET, plus the `LinqContraband.Scan` .NET tool, which is also published
only on NuGet. It is not distributed as a standalone ZIP installer, executable, or binary download. If another page offers a ZIP or installer and claims it is LinqContraband, treat that download as
untrusted.

## Safe Install

```bash
dotnet add package LinqContraband
```

The package is source-linked to the official GitHub repository and is intended to run as a compile-time analyzer in your
project.

## Reporting a Vulnerability

Use the [private vulnerability reporting form](https://github.com/georgepwall1991/LinqContraband/security/advisories/new)
for the official analyzer or scanner. Reports stay private to repository security maintainers. Include the affected
version, reproduction steps and likely impact; avoid publishing credentials or exploit details in an ordinary issue.
The maintainer aims to acknowledge reports within 14 days and prioritizes confirmed critical issues immediately.

Security fixes target the latest stable release. The [security policy](https://github.com/georgepwall1991/LinqContraband/blob/master/SECURITY.md)
explains supported versions, response targets and disclosure. Published fixes are documented in
[security advisories](https://github.com/georgepwall1991/LinqContraband/security/advisories) and release notes.

The scanner runs the target's .NET build, which can execute project-supplied code. Scan trusted repositories or use
an isolated environment; the scanner does not sandbox an untrusted build.

## If You Find an Impersonating Download

- Do not download, unzip, or run the suspicious file.
- Report the page through the host's abuse or malware-reporting flow.
- When reporting, include the official repository and NuGet package links above.
- If the suspicious result appears in search, report the result as malware or deceptive content to the search provider.
