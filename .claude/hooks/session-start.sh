#!/bin/bash
# SessionStart hook for Claude Code on the web: install the .NET SDK and restore
# packages so Claude can build and test before pushing.
#
# The sandbox proxy blocks builds.dotnet.microsoft.com (dotnet-install.sh), but the
# Ubuntu archive is reachable and ships dotnet-sdk-10.0, which satisfies global.json.
# Only the net10.0 runtime is installed: run tests with `-f net10.0`; CI covers
# net8.0 and net9.0.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

cd "${CLAUDE_PROJECT_DIR:-$(git rev-parse --show-toplevel)}"

if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
    echo 'export DOTNET_NOLOGO=1'
    echo 'export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1'
  } >> "$CLAUDE_ENV_FILE"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

if ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  if ! command -v apt-get >/dev/null 2>&1; then
    echo "session-start: apt-get not available; install the .NET 10 SDK manually" >&2
    exit 0
  fi
  SUDO=""
  if [ "$(id -u)" -ne 0 ]; then SUDO="sudo"; fi
  $SUDO apt-get update -qq
  DEBIAN_FRONTEND=noninteractive $SUDO apt-get install -y -qq dotnet-sdk-10.0 >/dev/null
fi

dotnet --version
dotnet tool restore
dotnet restore LinqContraband.sln
