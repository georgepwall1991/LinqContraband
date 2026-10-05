# Security Policy

## Official Distribution

LinqContraband is distributed as a NuGet analyzer package. The official packages are:

- https://www.nuget.org/packages/LinqContraband
- https://www.nuget.org/packages/LinqContraband.Scan

The canonical source repository is:

- https://github.com/georgepwall1991/LinqContraband

The project is maintained by George Wall:

- https://www.georgewall.uk/

The analyzer and scanner are distributed through NuGet, not as standalone ZIP installers or executable downloads. If a third-party page offers a ZIP,
installer, or binary download that claims to be LinqContraband, treat it as untrusted.

## Reporting Security Issues

Report vulnerabilities privately using [GitHub's private reporting form](https://github.com/georgepwall1991/LinqContraband/security/advisories/new).
Private vulnerability reporting is enabled for this repository. Reports are visible to repository security maintainers,
not in the public issue tracker. Do not include credentials, exploit details, or private source in a public issue.

Include the affected analyzer or scanner version, reproduction steps, expected and observed behavior, likely impact,
and any suggested mitigation. Use the report's private conversation for sensitive attachments and follow-up.

The maintainer aims to acknowledge reports within 14 calendar days and prioritizes confirmed critical vulnerabilities
for an immediate mitigation or fix. Confirmed medium-or-higher vulnerabilities should not remain unpatched for more
than 60 days after confirmation or public disclosure, whichever occurs first. These are response targets, not a claim about reports that have not occurred.
Coordinate disclosure in the private report; published advisories and release notes identify affected and fixed versions
and any assigned CVE or GHSA. See the [public advisories](https://github.com/georgepwall1991/LinqContraband/security/advisories).

## Supported Versions

Security fixes target the latest stable release. Upgrade to that release before reporting an issue unless the issue
prevents upgrading. Older versions are not promised a separate security backport.

## Security Boundaries

The analyzer inspects source code during compilation; it does not connect to a database or run the analyzed queries.
The scanner invokes `dotnet` to restore/build the target and optionally apply code fixes. A .NET build can execute
MSBuild targets and other project-supplied code. Run scans only on repositories you trust, or in an appropriately
isolated environment. The scanner is not a sandbox for untrusted projects.

CI defaults to read-only tokens. Release, NuGet OIDC publishing, documentation deployment, security-result uploads,
and explicitly authorized Claude implementation requests receive only their required job-level capabilities. Workflow actions and restored dependencies are pinned and checked for integrity.
Secret scanning, push protection, Dependabot alerts, and automatic security updates are enabled. These controls
supplement review and do not guarantee that every vulnerability or secret will be detected.

When reporting suspicious copies, malware, or impersonation:

- Do not download or run the suspicious archive.
- Include the suspicious URL, a screenshot, and the search query that found it.
- Point reviewers to the official NuGet package and canonical repository above.

For dependency use, prefer:

```bash
dotnet add package LinqContraband
```
