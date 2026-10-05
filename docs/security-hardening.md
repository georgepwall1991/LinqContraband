# Security hardening and Scorecard findings

On 2026-10-05, GitHub code scanning reported 54 open OpenSSF Scorecard findings
against master commit `235b7f6ba837b97774db5a67e3ffcca63e31f359`. These are
supply-chain and process findings, rather than confirmed exploitable defects in
analyzer rules. No findings were dismissed to hide an unresolved condition.

| Finding | Count | Remediation or remaining gate |
| --- | ---: | --- |
| Pinned dependencies | 32 | All external workflow actions use full commit SHAs resolved from upstream release tags. All six solution projects have committed NuGet lock files with content hashes. CI, publishing, CodeQL and agent setup explicitly restore in locked mode. Dependabot maintains pins and lock files. |
| Binary artifacts | 12 | Removed checked-in EF Core and Microsoft.Extensions DLLs. The existing `PackageDownload` entries restore the metadata and the build target copies it to the test output's `efcore/` folder. That folder is ignored in the source tree. Because NuGet lock files omit `PackageDownload`, the build also verifies SHA-512 hashes of all 12 restored metadata packages. |
| Token permissions | 6 | Build/test is read-only and uploads coverage instead of committing it. CodeQL and Claude have explicit read-only defaults. Release calls the reusable publishing workflow, removing `actions: write`. Two necessary job-level `contents: write` grants remain, explained below. |
| Branch protection | 1 | Master now requires PRs, up-to-date Build and Test (`build`) and CodeQL (`Analyze`) checks from GitHub Actions, linear history and resolved conversations. Admins are included; force pushes and deletion are disabled. An independent approval gate needs a second eligible reviewer; the Scorecard branch-protection score may remain below its passing threshold until that gate is added. |
| Fuzzing | 1 | FsCheck generates and shrinks scanner argument-parser inputs during every test run. Properties cover arbitrary argument sequences, nonnegative numeric round trips and unknown-option rejection, with 1,000 generated cases each. Scorecard action v2.4.4 uses Scorecard v5.5.0, which recognizes C# FsCheck integration. |
| Code review | 1 | Historical approved-change ratio cannot be repaired by editing code. Only the owner is currently a collaborator. Add an eligible independent reviewer and require approvals; future approved PRs will improve this finding. Do not manufacture approvals or rewrite history. |
| OpenSSF best practices | 1 | Maintainer enrollment and evidence-backed self-assessment at [OpenSSF Best Practices](https://www.bestpractices.dev/) remain required. Do not add an unearned badge. |

## Necessary write grants

`release.yml` grants `contents: write` only to its release job, after a successful
master push build (or an explicit master dispatch). It creates the version tag and
GitHub Release. The publishing job has only `contents: read` and OIDC issuance.
The reusable workflow remains `publish.yml`, preserving the workflow identity
used by the existing NuGet trusted-publishing policy. A real publish is validated
only by a future authorized release, not by local workflow linting.

`claude.yml` grants `contents: write` only to the on-demand implementation job.
The existing owner/member/collaborator and explicit `@claude` checks remain in
place. It needs this grant and checkout credentials to push requested fixes. Automatic review is a separate
read-only job. Removing that grant would break the authorized implementation flow.
Scorecard documents sensitive job-level write permissions even when they are
necessary; these two warnings remain visible for review.

## Updating dependencies

Change the intended package version and run `dotnet restore LinqContraband.sln`
to regenerate the lock files. Review the dependency graph and content-hash changes,
then commit the affected lock files with the project change. Validate with
`dotnet restore LinqContraband.sln --locked-mode`. Do not remove locked mode to
work around a failed restore. The analyzer's Roslyn 4.3.0 compatibility pin stays
unchanged.

The link-check bootstrap also verifies a committed SHA-256 before extracting or running its downloaded executable, with native Linux x64 and macOS arm64 archives.

For actions, resolve the upstream release tag to its commit (dereferencing
annotated tags), review the upstream changes and update the SHA and version
comment together. Dependabot's GitHub Actions configuration keeps these pins
maintained.

## Closure evidence

Repository changes need PR review and merge, followed by a new Scorecard scan of
master. Local test results and PR CI alone do not close default-branch alerts.
Check the analyzed SHA before reporting that findings are fixed. External badge
enrollment and historical review metrics have their own evidence requirements.

See the [Scorecard check documentation](https://github.com/ossf/scorecard/blob/main/docs/checks.md)
for the distinctions between configuration warnings, historical metrics and
confirmed vulnerability reports.
