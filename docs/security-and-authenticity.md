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
- Maintainer: [George Wall](https://www.georgewall.uk/)

LinqContraband is a NuGet analyzer package for .NET. It is not distributed as a standalone ZIP installer, executable, or
binary download. If another page offers a ZIP or installer and claims it is LinqContraband, treat that download as
untrusted.

## Safe Install

```bash
dotnet add package LinqContraband
```

The package is source-linked to the official GitHub repository and is intended to run as a compile-time analyzer in your
project.

## If You Find an Impersonating Download

- Do not download, unzip, or run the suspicious file.
- Report the page through the host's abuse or malware-reporting flow.
- When reporting, include the official repository and NuGet package links above.
- If the suspicious result appears in search, report the result as malware or deceptive content to the search provider.
