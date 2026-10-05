#!/usr/bin/env bash
# Offline link check for the docs site, README/CONTRIBUTING and analyzer help links.
#
# Usage: scripts/check-links.sh <site-dir>
#   <site-dir> holds the built Jekyll site under LinqContraband/ (the Pages baseurl),
#   e.g. `jekyll build -s docs -d _site/LinqContraband` then `scripts/check-links.sh _site`.
#
# Links to the published site and to this repo's master branch are remapped onto the
# local build and checkout, so broken pages, files and #fragments fail the check without
# any network access. Other external URLs are skipped (--offline).
set -euo pipefail

LYCHEE_VERSION="0.24.2"

site_dir="$(cd "${1:?usage: scripts/check-links.sh <site-dir>}" && pwd)"
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"

if [[ ! -f "$site_dir/LinqContraband/index.html" ]]; then
  echo "No built site at $site_dir/LinqContraband (expected index.html)" >&2
  exit 1
fi

lychee_bin="$(command -v lychee || true)"
if [[ -z "$lychee_bin" ]]; then
  # Pin downloaded executable bytes before extracting or running them.
  case "$(uname -s)-$(uname -m)" in
    Linux-x86_64)
      target="x86_64-unknown-linux-gnu"
      sha256="1f4e0ef7f6554a6ed33dd7ac144fb2e1bbed98598e7af973042fc5cd43951c9a"
      ;;
    Darwin-arm64)
      target="aarch64-apple-darwin"
      sha256="c9d3740ea2d891854d37116c9fba840f37b6e7c89d330e7db84ac333631c4977"
      ;;
    *)
      echo "Install lychee ${LYCHEE_VERSION} for this platform before running the link check." >&2
      exit 1
      ;;
  esac
  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' EXIT
  archive="$tmp/lychee.tar.gz"
  curl -sSfL "https://github.com/lycheeverse/lychee/releases/download/lychee-v${LYCHEE_VERSION}/lychee-${target}.tar.gz" -o "$archive"
  if [[ "$(uname -s)" == "Linux" ]]; then
    printf '%s  %s\n' "$sha256" "$archive" | sha256sum -c
  else
    printf '%s  %s\n' "$sha256" "$archive" | shasum -a 256 -c
  fi
  tar xzf "$archive" -C "$tmp"
  lychee_bin="$tmp/lychee-${target}/lychee"
fi

"$lychee_bin" --version
"$lychee_bin" \
  --offline \
  --include-fragments \
  --no-progress \
  --root-dir "$site_dir" \
  --remap "^https://georgepwall1991\.github\.io/LinqContraband/?$ file://$site_dir/LinqContraband/index.html" \
  --remap "^https://georgepwall1991\.github\.io/LinqContraband/(.*)$ file://$site_dir/LinqContraband/\$1" \
  --remap "^https://github\.com/georgepwall1991/LinqContraband/(?:blob|tree)/master/(.*)$ file://$repo_dir/\$1" \
  README.md CONTRIBUTING.md SECURITY.md \
  'src/LinqContraband/**/*.cs' \
  "$site_dir/LinqContraband/**/*.html"
