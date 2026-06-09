#!/usr/bin/env bash
# PreToolUse guard — blocks manual edits to GENERATED files so CI never fails on drift.
#
# docs/rule-catalog.md is generated from src/LinqContraband/Catalog/RuleCatalog.cs.
# CI (.github/workflows/dotnet.yml) runs the generator with `--check` and fails the
# build if the checked-in file is stale or hand-edited.
#
# This script is intentionally dependency-free and fast (one shell spawn, no jq/python)
# so it adds no meaningful latency to the edit loop. Exit 2 => block + reason to Claude.

input=$(cat)

# Extract the first "file_path":"..." value from the tool input JSON (Write/Edit/MultiEdit).
file_path=$(printf '%s' "$input" | sed -n 's/.*"file_path"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')

case "$file_path" in
  */docs/rule-catalog.md|docs/rule-catalog.md)
    cat >&2 <<'MSG'
BLOCKED: docs/rule-catalog.md is a GENERATED file — do not hand-edit it.

CI runs `RuleCatalogDocGenerator -- --check` and will fail if this file is edited
directly or left stale.

Instead:
  1. Edit the source of truth: src/LinqContraband/Catalog/RuleCatalog.cs
  2. Regenerate: dotnet run --project tools/RuleCatalogDocGenerator/RuleCatalogDocGenerator.csproj -- --write
MSG
    exit 2
    ;;
esac

exit 0
