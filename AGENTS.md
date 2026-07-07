# LinqContraband — agent router

Mac-wide automation: **`~/automation/AGENTS.md`** · jump: `gf` then `cd LinqContraband`

Roslyn analyzer repo. Package: `LinqContraband` (NuGet). Health tracker: **`docs/analyzer-health.md`**.

## Start here

- **Analyzer hardening loop:** invoke `/analyzer-health-release` — read health doc, pick next high-payoff rule, TDD harden, open PR. Merge/release only when explicitly authorized.
- **.NET patterns:** read `~/.claude/resources/roslyn-analyzer-patterns.md` on demand.
- **Build/test:** `dbt` / `dtt` from repo root (or `dotnet test` on `LinqContraband.sln`).
- **Cross-review:** `codex review --uncommitted` before PR on non-trivial changes.

## Load-bearing facts

- One focused rule per batch unless health doc names a tightly coupled pair.
- Version bumps, changelog, tag, and NuGet publish follow `references/release-mechanics.md` in the analyzer-health-release skill.
- Conventional commits; push-to-main is hook-blocked — branch → PR → squash.