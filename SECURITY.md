# Security Policy

## Official Distribution

LinqContraband is distributed as a NuGet analyzer package. The official package is:

- https://www.nuget.org/packages/LinqContraband

The canonical source repository is:

- https://github.com/georgepwall1991/LinqContraband

The project is maintained by George Wall:

- https://www.georgewall.uk/

LinqContraband is not distributed as a standalone ZIP installer or executable. If a third-party page offers a ZIP,
installer, or binary download that claims to be LinqContraband, treat it as untrusted.

## Reporting Security Issues

Please report vulnerabilities in the official project through GitHub Security Advisories or by opening a private report
from the Security tab of the canonical repository.

When reporting suspicious copies, malware, or impersonation:

- Do not download or run the suspicious archive.
- Include the suspicious URL, a screenshot, and the search query that found it.
- Point reviewers to the official NuGet package and canonical repository above.

For dependency use, prefer:

```bash
dotnet add package LinqContraband
```
