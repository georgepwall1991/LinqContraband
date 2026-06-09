---
name: release-prep
description: Prepare a LinqContraband release — bump the package version, write matching CHANGELOG and PackageReleaseNotes entries (same LC rule ids, enforced by a test), regenerate the rule catalog, run the full CI gate, and draft the GitHub release notes that trigger the NuGet publish. Use when cutting a new LinqContraband version.
disable-model-invocation: true
---

# Prepare a LinqContraband Release

Drives the repo's **two-PR release cadence**:
1. **Fix PR** — the behavioural change (already merged by the time you run this).
2. **Release-prep PR** (this skill) — version bump + changelog/release-notes.
3. After it merges, publishing a **GitHub Release** triggers
   `.github/workflows/publish.yml` → `dotnet pack` → `dotnet nuget push`.

## Hard contract: CHANGELOG ↔ PackageReleaseNotes must reference the same LC ids

A test enforces that the new `CHANGELOG.md` version entry and the
`<PackageReleaseNotes>` in `src/LinqContraband/LinqContraband.csproj` mention the
**same set of LC rule ids**. Keep them in lockstep or CI fails.

## Steps

1. **Pick the version.** Read the current `<Version>` in `src/LinqContraband/LinqContraband.csproj`
   (e.g. `5.4.12`). Choose the next number based on the change type:
   - patch (`5.4.x`) — false-positive/negative fixes, doc-only, internal refactors
   - minor (`5.x.0`) — new rule(s) or new opt-in behaviour
   Confirm the chosen version with the user if ambiguous.

2. **Gather what shipped.** `git log <last-release-tag>..HEAD --oneline` (or inspect merged
   PRs since the last `chore: prepare … release` commit). Collect the affected **LC ids** and a
   one-line human summary per change.

3. **Bump the version** in `src/LinqContraband/LinqContraband.csproj` (`<Version>`).

4. **Update `<PackageReleaseNotes>`** in the same csproj — a concise prose summary that names
   every affected LC id. (Remember XML-escape `<`, `>`, `&` — e.g. `=&gt;`.)

5. **Add the `CHANGELOG.md` entry** at the top, same version, naming the **same LC ids** as the
   release notes. Match the existing changelog heading/format.

6. **Regenerate the catalog doc** (never hand-edit `docs/rule-catalog.md`):
   ```bash
   dotnet run --project tools/RuleCatalogDocGenerator/RuleCatalogDocGenerator.csproj -- --write
   ```

7. **Run the full CI gate locally:**
   ```bash
   dotnet build
   dotnet test
   dotnet run --project tools/SampleDiagnosticsVerifier/SampleDiagnosticsVerifier.csproj --configuration Release -- --frameworks net8.0 net9.0 net10.0
   ```
   The release/changelog consistency test and `RuleCatalogIntegrityTests` must both pass.
   Also confirm the catalog `--check` is clean:
   ```bash
   dotnet run --project tools/RuleCatalogDocGenerator/RuleCatalogDocGenerator.csproj -- --check
   ```

8. **Draft the release-prep commit & PR.** Suggest:
   - branch: `chore/prepare-<version>-release`
   - commit: `chore: prepare <version> release — publishes <LCxxx> <short summary>`
   Do not commit/push unless asked.

9. **Draft the GitHub Release notes** (the publish trigger). Hand the user copy-ready notes:
   title `v<version>`, body summarising the LC fixes/additions. Remind them that publishing the
   GitHub Release is what fires `publish.yml` and pushes to NuGet (`--skip-duplicate`), and that
   the `NUGET_API_KEY` secret must be set in the repo.

## Finish

Report: version bumped from→to, the LC ids referenced (confirming CHANGELOG and PackageReleaseNotes
match), gate results, and the ready-to-paste GitHub Release notes.
