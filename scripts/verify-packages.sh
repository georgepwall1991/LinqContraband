#!/usr/bin/env bash
set -euo pipefail

package_dir="${1:-artifacts/packages}"
root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root_dir"

analyzer_version="$(dotnet msbuild src/LinqContraband/LinqContraband.csproj -getProperty:Version -nologo | tr -d '[:space:]')"
analyzer_package="$package_dir/LinqContraband.$analyzer_version.nupkg"

if [[ ! -f "$analyzer_package" ]]; then
  echo "Missing analyzer package: $analyzer_package" >&2
  exit 1
fi

cmp README.md <(unzip -p "$analyzer_package" README.md)

# Product-flow visuals referenced by PackageReadmeFile must ship inside the package.
for asset in \
  assets/flow-ide-diagnostics.svg \
  assets/flow-before-after-fix.svg \
  assets/flow-analyzer-ci-loop.svg
do
  cmp "$asset" <(unzip -p "$analyzer_package" "$asset")
done

# Discoverability metadata: high-intent EF Core / LINQ terms (NuGet search).
analyzer_nuspec="$(unzip -p "$analyzer_package" LinqContraband.nuspec)"

for term in \
  "EF Core" \
  "LINQ" \
  "N+1" \
  "client-side evaluation" \
  "AsNoTracking" \
  "DbContext" \
  "roslyn" \
  "roslyn-analyzer" \
  "NPlusOne" \
  "query-performance"
do
  printf '%s' "$analyzer_nuspec" | grep -Fq "$term" || {
    echo "Analyzer nuspec missing discoverability term: $term" >&2
    exit 1
  }
done

# Absolute HTTPS images in packed README (NuGet rendering).
python3 -c '
import re, sys, zipfile, pathlib
pkg = pathlib.Path(sys.argv[1])
with zipfile.ZipFile(pkg) as z:
    readme = z.read("README.md").decode("utf-8")
refs = re.findall(r"!\[[^\]]*\]\(([^)]+)\)", readme) + re.findall(r"<img[^>]+src=\"([^\"]+)\"", readme)
bad = [r for r in refs if not r.startswith("https://")]
if bad:
    raise SystemExit("Packed README has non-HTTPS image refs: " + ", ".join(bad))
for asset in (
    "assets/flow-ide-diagnostics.svg",
    "assets/flow-before-after-fix.svg",
    "assets/flow-analyzer-ci-loop.svg",
):
    if f"https://raw.githubusercontent.com/georgepwall1991/LinqContraband/master/{asset}" not in readme:
        raise SystemExit(f"Packed README missing absolute visual URL for {asset}")
print("Packed README HTTPS image URLs OK")
' "$analyzer_package"

echo "Verified package version, README, assets, and discoverability metadata for $analyzer_version."
