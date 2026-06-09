#!/usr/bin/env bash
# PostToolUse auto-regenerator — keeps the GENERATED catalog doc in sync with its source.
#
# docs/rule-catalog.md is generated from src/LinqContraband/Catalog/RuleCatalog.cs.
# The companion PreToolUse hook (guard-generated.sh) BLOCKS hand-edits to the generated
# file; this hook closes the other half of the loop — whenever RuleCatalog.cs itself is
# edited, it regenerates docs/rule-catalog.md so CI's `--check` step never fails on drift.
#
# The generator writes the doc via `dotnet run` (filesystem), NOT via the Edit tool, so it
# does not re-trigger guard-generated.sh. This hook only does work when RuleCatalog.cs was
# the edited file; for every other edit it exits immediately (one sed, no build).
#
# Exit codes (PostToolUse): 0 => silent/ok, 2 => surface message back to Claude.

input=$(cat)

# Extract the first "file_path":"..." value from the tool input JSON (Write/Edit/MultiEdit).
file_path=$(printf '%s' "$input" | sed -n 's/.*"file_path"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')

case "$file_path" in
  */Catalog/RuleCatalog.cs)
    gen="$CLAUDE_PROJECT_DIR/tools/RuleCatalogDocGenerator/RuleCatalogDocGenerator.csproj"
    if output=$(dotnet run --project "$gen" -- --write 2>&1); then
      echo "[regen-catalog] RuleCatalog.cs changed → regenerated docs/rule-catalog.md" >&2
      exit 0
    else
      {
        echo "[regen-catalog] FAILED to regenerate docs/rule-catalog.md after editing RuleCatalog.cs."
        echo "Fix the build/catalog error below, then rerun:"
        echo "  dotnet run --project tools/RuleCatalogDocGenerator/RuleCatalogDocGenerator.csproj -- --write"
        echo "--- generator output ---"
        printf '%s\n' "$output"
      } >&2
      exit 2
    fi
    ;;
esac

exit 0
