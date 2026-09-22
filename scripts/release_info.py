#!/usr/bin/env python3
"""Release metadata helper shared by CI, release.yml, and publish.yml.

The csproj <Version> is the single source of truth for a release. Every
release also needs a matching `## [X.Y.Z]` section in CHANGELOG.md, whose
body becomes the GitHub Release notes.

Usage:
  release_info.py version             print the csproj Version
  release_info.py notes [VERSION]     print the CHANGELOG section for VERSION
                                      (defaults to the csproj Version)
  release_info.py check [--tag TAG]   fail unless the csproj Version has a
                                      CHANGELOG section and, when --tag is
                                      given, TAG is exactly v<Version>
"""

from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CSPROJ = ROOT / "src" / "LinqContraband" / "LinqContraband.csproj"
CHANGELOG = ROOT / "CHANGELOG.md"
SEMVER = re.compile(r"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$")


def fail(message: str) -> None:
    print(f"::error::{message}", file=sys.stderr)
    sys.exit(1)


def csproj_version() -> str:
    versions = [el.text.strip() for el in ET.parse(CSPROJ).iter("Version") if el.text]
    if len(versions) != 1:
        fail(f"Expected exactly one <Version> in {CSPROJ.relative_to(ROOT)}, found {len(versions)}")
    version = versions[0]
    if not SEMVER.match(version):
        fail(f"csproj Version '{version}' is not a semantic version")
    return version


def changelog_section(version: str) -> str | None:
    heading = re.compile(rf"^## \[{re.escape(version)}\](?:\s|$)")
    lines = CHANGELOG.read_text(encoding="utf-8").splitlines()
    for start, line in enumerate(lines):
        if heading.match(line):
            end = next(
                (i for i in range(start + 1, len(lines)) if lines[i].startswith("## ")),
                len(lines),
            )
            body = "\n".join(lines[start + 1 : end]).strip()
            return body or None
    return None


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("version")
    notes = sub.add_parser("notes")
    notes.add_argument("version", nargs="?")
    check = sub.add_parser("check")
    check.add_argument("--tag")
    args = parser.parse_args()

    if args.command == "version":
        print(csproj_version())
        return

    if args.command == "notes":
        version = args.version or csproj_version()
        body = changelog_section(version)
        if body is None:
            fail(f"CHANGELOG.md has no non-empty '## [{version}]' section")
        print(body)
        return

    version = csproj_version()
    if args.tag is not None and args.tag != f"v{version}":
        fail(f"Tag '{args.tag}' does not match csproj Version '{version}' (expected 'v{version}')")
    if changelog_section(version) is None:
        fail(f"CHANGELOG.md has no non-empty '## [{version}]' section for csproj Version {version}")
    suffix = f" (tag {args.tag})" if args.tag else ""
    print(f"Release metadata OK for {version}{suffix}: CHANGELOG section found")


if __name__ == "__main__":
    main()
