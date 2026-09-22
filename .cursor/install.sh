#!/usr/bin/env bash
# Cloud Agent install phase for LinqContraband.
#
# Idempotent, non-interactive setup: installs the .NET SDK/runtimes the repo
# pins (see global.json + the net8/net9/net10 target frameworks), then restores
# and builds the solution so the analyzer, tools, and tests are ready to run.
#
# Safe to run repeatedly and from either a plain base image or a snapshot that
# already has the SDK installed.
set -euo pipefail

DOTNET_DIR="${DOTNET_ROOT:-$HOME/.dotnet}"
export DOTNET_ROOT="$DOTNET_DIR"
export PATH="$DOTNET_DIR:$DOTNET_DIR/tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

install_dotnet() {
  local script="/tmp/dotnet-install.sh"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$script"
  chmod +x "$script"
  # SDK 10 satisfies global.json (10.0.100, rollForward latestFeature) and builds
  # every target framework; the 8.0/9.0 runtimes let the multi-targeted tests and
  # sample execute on those TFMs.
  bash "$script" --channel 10.0 --install-dir "$DOTNET_DIR"
  bash "$script" --channel 9.0 --runtime dotnet --install-dir "$DOTNET_DIR"
  bash "$script" --channel 8.0 --runtime dotnet --install-dir "$DOTNET_DIR"
}

need_dotnet=1
if command -v dotnet >/dev/null 2>&1; then
  # Confirm a 10.x SDK is present (global.json requires it); otherwise (re)install.
  if dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
    need_dotnet=0
  fi
fi

if [[ "$need_dotnet" -eq 1 ]]; then
  install_dotnet
fi

# Make dotnet discoverable in future interactive shells without duplicating lines.
profile="$HOME/.bashrc"
marker="# >>> linqcontraband dotnet env >>>"
if ! grep -qF "$marker" "$profile" 2>/dev/null; then
  {
    echo "$marker"
    echo "export DOTNET_ROOT=\"$DOTNET_DIR\""
    echo "export PATH=\"$DOTNET_DIR:$DOTNET_DIR/tools:\$PATH\""
    echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
    echo "export DOTNET_NOLOGO=1"
    echo "# <<< linqcontraband dotnet env <<<"
  } >> "$profile"
fi

dotnet --info
dotnet restore LinqContraband.sln
dotnet build LinqContraband.sln --no-restore
