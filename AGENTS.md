# LinqContraband — agent router

Mac-wide automation: **`~/automation/AGENTS.md`** · jump: `gf` then `cd LinqContraband`

Roslyn analyzer repo. Package: `LinqContraband` (NuGet). Health tracker: **`docs/analyzer-health.md`**.

## Start here

- **Analyzer hardening loop:** invoke `/analyzer-health-release` — read health doc, pick next high-payoff rule, TDD harden, open PR. Merge/release only when explicitly authorized.
- **.NET patterns:** read `~/.claude/resources/roslyn-analyzer-patterns.md` on demand.
- **Build/test:** `dbt` / `dtt` from repo root (or `dotnet test` on `LinqContraband.sln`).
- **Cross-review:** `codex review --uncommitted` before PR on non-trivial changes.

## Analyzer navigation gate

Before broad text search or release review on analyzer work, use semantic tooling first:

- **dotnet-lsp first:** run workspace/document symbol lookup for the target rule, then references on changed helpers or public entry points, and use diagnostics when available before full tests.
- **roslyn MCP:** callers / impact / unused-code before renames (project `.grok/config.toml` + `.mcp.json`; `DOTNET_ROLL_FORWARD=LatestMajor`).
- **Rider MCP when exposed:** use Rider-backed project context or inspections when the `rider` MCP is available (Grok uses JetBrains stdio proxy, not SSE).
- **Text search second:** use code search/power scripts only to confirm semantic findings, locate non-C# artifacts, or when LSP/Rider is unavailable; state the fallback in the final summary.

Skill: **`dotnet-semantic-workflow`**. Health check after MCP changes: `grok mcp doctor`.

## Load-bearing facts

- One focused rule per batch unless health doc names a tightly coupled pair.
- Version bumps, changelog, tag, and NuGet publish follow `references/release-mechanics.md` in the analyzer-health-release skill.
- Conventional commits; push-to-main is hook-blocked — branch → PR → squash.
