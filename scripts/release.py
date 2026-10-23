#!/usr/bin/env python3
"""Work out the next release version from Conventional Commits and apply it.

Usage:
  python3 scripts/release.py plan  [--bump auto|patch|minor|major] [--github-output]
  python3 scripts/release.py apply --version X.Y.Z [--notes PATH]

Only commits since the last vX.Y.Z tag that touch what ships in the zips count (PACKAGED below). CI, tooling and
documentation-only commits never change the version. Among the counted commits:
  - a breaking change (`type!:` or a `BREAKING CHANGE:` footer) bumps major
  - `feat:` bumps minor
  - anything else (fix, perf, refactor, build, docs, non-conventional…) bumps patch
`--bump` overrides the computed bump, e.g. to release a change made outside PACKAGED (like the bundled BepInEx).

`plan` prints the decision (and writes `version`, `bump`, `last_tag` to $GITHUB_OUTPUT with --github-output).
`apply` writes the version into Directory.Build.props and every plugin's Info.Version, prepends a CHANGELOG.md section
generated from the commits, and writes release notes (that section plus scripts/release-notes-footer.md).
"""
import argparse, os, re, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PACKAGED = ["src", "Directory.Build.props", "docs/INSTALL.txt", "LICENSE"]
CONVENTIONAL = re.compile(r"^(?P<type>[a-z]+)(?:\((?P<scope>[^)]+)\))?(?P<bang>!)?: (?P<desc>.+)$")
BREAKING_FOOTER = re.compile(r"^BREAKING[ -]CHANGE: ", re.M)
RANK = {"none": 0, "patch": 1, "minor": 2, "major": 3}
SECTIONS = [("breaking", "Breaking changes"), ("feat", "Features"), ("fix", "Fixes"), ("other", "Other changes")]


def git(*args):
    return subprocess.run(["git", *args], cwd=ROOT, check=True, capture_output=True, text=True).stdout


def last_tag():
    tags = [t for t in git("tag", "--merged", "HEAD", "--list", "v*", "--sort=-v:refname").split() if re.fullmatch(r"v\d+\.\d+\.\d+", t)]
    return tags[0] if tags else None


def commits_since(tag, packaged_only=True):
    """Commits since `tag` (touching PACKAGED paths unless packaged_only is False), oldest first: (sha, subject, body)."""
    rng = f"{tag}..HEAD" if tag else "HEAD"
    paths = ["--", *PACKAGED] if packaged_only else []
    out = git("log", "--reverse", "--format=%H%x1f%s%x1f%b%x1e", rng, *paths)
    commits = []
    for rec in out.split("\x1e"):
        rec = rec.strip("\n")
        if rec:
            sha, subject, body = rec.split("\x1f")
            commits.append((sha, subject, body))
    return commits


def classify(subject, body):
    """-> (kind, bump, text). kind is a SECTIONS key; release commits return bump 'none'."""
    m = CONVENTIONAL.match(subject)
    if not m:
        return "other", "patch", subject
    typ, scope, desc = m["type"], m["scope"], m["desc"]
    if typ == "chore" and scope == "release":
        return "other", "none", subject
    text = f"**{scope}:** {desc}" if scope else desc
    if m["bang"] or BREAKING_FOOTER.search(body):
        return "breaking", "major", text
    if typ == "feat":
        return "feat", "minor", text
    if typ in ("fix", "perf", "revert"):
        return "fix", "patch", text
    return "other", "patch", text


def bump_version(version, bump):
    major, minor, patch = map(int, version.split("."))
    if bump == "major":
        return f"{major + 1}.0.0"
    if bump == "minor":
        return f"{major}.{minor + 1}.0"
    return f"{major}.{minor}.{patch + 1}"


def current_version():
    m = re.search(r"<Version>(\d+\.\d+\.\d+)</Version>", (ROOT / "Directory.Build.props").read_text())
    if not m:
        sys.exit("no <Version> in Directory.Build.props")
    return m[1]


def plan(args):
    tag = last_tag()
    base = tag[1:] if tag else current_version()
    entries = []
    bump = "none"
    for sha, subject, body in commits_since(tag):
        kind, b, text = classify(subject, body)
        if b == "none":
            continue
        entries.append((sha, subject, kind, b))
        if RANK[b] > RANK[bump]:
            bump = b
    if args.bump != "auto":
        bump = args.bump
    version = bump_version(base, bump) if bump != "none" else ""

    print(f"last tag: {tag or '(none)'}; packaged paths: {', '.join(PACKAGED)}")
    if not entries:
        print("no commits touch packaged paths since the last tag")
    for sha, subject, kind, b in entries:
        flag = "" if CONVENTIONAL.match(subject) else "   <- not a Conventional Commit, counted as patch"
        print(f"  {sha[:7]} [{b:5}] {subject}{flag}")
    print(f"bump: {bump}" + (f" (forced)" if args.bump != "auto" else "") + (f" -> v{version}" if version else " (no release)"))
    if args.github_output and os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a") as f:
            f.write(f"version={version}\nbump={bump}\nlast_tag={tag or ''}\n")


def changelog_section(version, tag):
    groups = {key: [] for key, _ in SECTIONS}

    def collect(commits, skip_types=()):
        for sha, subject, body in commits:
            m = CONVENTIONAL.match(subject)
            if m and m["type"] in skip_types:
                continue
            kind, b, text = classify(subject, body)
            if b != "none":
                groups[kind].append(f"- {text} ({sha[:7]})")

    collect(commits_since(tag))
    if not any(groups.values()):
        # A forced bump with no packaged changes (e.g. a newer bundled BepInEx): list the other relevant commits.
        collect(commits_since(tag, packaged_only=False), skip_types=("ci", "docs", "test", "style"))
    lines = [f"## {version}"]
    for key, title in SECTIONS:
        if groups[key]:
            lines += ["", f"### {title}", *groups[key]]
    if not any(groups.values()):
        lines += ["", "- Rebuild with no changes to the fixes."]
    return "\n".join(lines) + "\n"


def apply(args):
    version = args.version
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        sys.exit(f"bad version {version!r}")
    props = ROOT / "Directory.Build.props"
    text, n = re.subn(r"<Version>\d+\.\d+\.\d+</Version>", f"<Version>{version}</Version>", props.read_text())
    if n != 1:
        sys.exit("expected exactly one <Version> in Directory.Build.props")
    props.write_text(text)
    plugins = sorted((ROOT / "src").glob("*/Plugin.cs"))
    for p in plugins:
        text, n = re.subn(r'public const string Version = "\d+\.\d+\.\d+";', f'public const string Version = "{version}";', p.read_text())
        if n != 1:
            sys.exit(f"expected exactly one Info.Version in {p}")
        p.write_text(text)

    section = changelog_section(version, last_tag())
    changelog = ROOT / "CHANGELOG.md"
    old = changelog.read_text()
    if f"\n## {version}\n" in f"\n{old}":
        sys.exit(f"CHANGELOG.md already has a {version} section")
    head, sep, rest = old.partition("\n## ")
    changelog.write_text(f"{head.rstrip()}\n\n{section}\n{sep.lstrip(chr(10))}{rest}" if sep else f"{old.rstrip()}\n\n{section}")

    if args.notes:
        footer = (ROOT / "scripts" / "release-notes-footer.md").read_text().replace("{version}", version)
        body = section.split("\n", 1)[1].strip()
        Path(args.notes).write_text(f"{body}\n\n{footer}")
    print(f"applied v{version} to {props.name}, {len(plugins)} plugins and CHANGELOG.md")


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("plan")
    p.add_argument("--bump", choices=["auto", "patch", "minor", "major"], default="auto")
    p.add_argument("--github-output", action="store_true")
    a = sub.add_parser("apply")
    a.add_argument("--version", required=True)
    a.add_argument("--notes")
    args = ap.parse_args()
    (plan if args.cmd == "plan" else apply)(args)


if __name__ == "__main__":
    main()
