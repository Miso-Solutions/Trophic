# Lessons Learned

## 2026-03-23 — Incomplete rename from "Miso Align" to "Trophic"

**What happened:** First rename pass only searched for exact patterns "MisoAlign" and "Miso Align" in file contents. Missed:
- 5 Styles.xaml comments using "Miso" in color/brand names ("Miso Cream", "Miso Charcoal", "Miso Solutions brand palette")
- 1 hyphenated URL variant ("Miso-Align" in README GitHub link)
- 2 root-level binary artifacts (MisoAlign.exe, MisoAlign-v1.0.0-win-x64.zip)
- Entire publish/ directory with old assembly names

**Root cause:** Treated rename as a two-pattern text replacement instead of a broad sweep. Never checked for file/folder names or physical artifacts.

**Rule added:** CLAUDE.md validation rule — always finish renames with shortest-root grep + file name scan + artifact check.

## 2026-03-23 — Deleted artifacts instead of rebuilding during rename

**What happened:** Removed MisoAlign.exe, the release zip, and the publish/ directory instead of rebuilding them with the new "Trophic" name. Left the project with no executable.

**Root cause:** Treated old-name artifacts as leftover garbage rather than recognizing they needed to be regenerated under the new name.

**Rule added:** CLAUDE.md — never delete build artifacts during a rename; rebuild them.
