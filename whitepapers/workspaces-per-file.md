# Proposal: Per-workspace files under ~/.a2c/workspaces

Date: 2025-08-21
Status: Draft
Owner: Paul (with collaboration)

## Summary

Move from a single monolithic `~/.a2c/workspaces.xfer` to a directory of per-workspace files under `~/.a2c/workspaces`. Keep global settings in a small, separate file. This improves isolation, preserves formatting/comments, reduces merge conflicts, and enables simpler sharing and templating of workspaces.

## Goals

- Preserve authorship formatting and comments; avoid wholesale re-serialize of a large file.
- Allow independent creation, editing, and sharing of workspaces as discrete files.
- Detect and report naming clashes clearly and early.
- Maintain a smooth migration path and backward compatibility.
- Keep runtime discovery fast and predictable.

## Non-goals (for initial phase)

- Changing the internal XferLang representation beyond what’s needed for file splitting.
- Introducing YAML-as-storage (YAML import can remain a possible future feature).
- Deep refactors of scripting/runtime; this is primarily configuration layout and IO semantics.

## Proposed layout

- Global config (small):
  - `~/.a2c/config.xfer` (global macros, scripts, feature flags, defaults, paths)
- Workspaces (directory-per-workspace by default):
  - `~/.a2c/workspaces/<slug>/workspace.xfer` (required)

Rationale: Directory-per-workspace enables clean separation of assets, scripts, request collections, and optional per-workspace packages. XferLang includes/imports make it easy to split content across files and load them deterministically. The internal `Name` field within `workspace.xfer` remains the source of truth for identity and clash detection.

### Slug rules

- Filename slug should be stable, human-friendly, and filesystem-safe.
- Suggested: lowercase, ASCII, dash-separated; strip/replace spaces and punctuation.
- Example: `"My API (Prod)" → my-api-prod.xfer`.
- Display name remains inside the file (`Name` field). Require uniqueness case-insensitively.

### Workspace folder structure

Minimal:

- `~/.a2c/workspaces/<slug>/workspace.xfer` — primary workspace definition (required).

Common optional layout:

- `~/.a2c/workspaces/<slug>/scripts/` — XferLang and/or JS files auto-included during workspace load.
- `~/.a2c/workspaces/<slug>/requests/` — large request collections, optionally referenced from `workspace.xfer`.
- `~/.a2c/workspaces/<slug>/packages/` — optional per-workspace package restore root (see Packages and version isolation).
- `~/.a2c/workspaces/<slug>/README.md` — human docs.

Inclusion semantics:

- By default, on load, auto-include `scripts/**/*.{xfer,js}` in deterministic order (case-insensitive, ordinal, depth-first) unless explicitly disabled via a flag in `workspace.xfer`.
- `workspace.xfer` can also contain explicit include patterns for `requests/` and additional script locations to retain full control over load order.

## Discovery and precedence

- On startup or `reload`:
  1. Load `~/.a2c/config.xfer` if present (global defaults/macros).
  2. Enumerate `~/.a2c/workspaces/*/workspace.xfer` (each subdirectory with a `workspace.xfer` is a workspace).
  3. Ignore temp/backup/hidden artifacts by default (`*~`, `*.bak`, `.swp`, files starting with `.`), unless an advanced flag is set.
  4. Parse each workspace; build a map by `Name`.
  5. On name collisions, fail fast with a clear error listing the filenames and the duplicate `Name` values.

- Disabled workspaces: support either a filename suffix (e.g., `foo.xfer.disabled`) or an `enabled: false` flag in the file. The suffix approach avoids parsing disabled files and is more obvious in the filesystem.

Note: If both a legacy monolithic `~/.a2c/workspaces.xfer` and per-folder workspaces are present, per-folder workspaces take precedence for identical `Name` values. A deprecation warning should be emitted when loading legacy files.

## Editing semantics (preserve formatting)

- Treat each workspace file as the source of truth; avoid re-serializing unrelated parts.
- When the CLI edits a workspace file, prefer minimal, targeted changes and preserve ordering/whitespace when possible.
- Use atomic writes (write to temp, fsync, then rename) to prevent partial writes.
- Use file locks or retry-on-locked semantics to avoid races with external editors and file watchers.
- Prefer creating new files vs. mutating existing ones unless explicitly asked.

## Import and defaults

- Import UX: allow `workspace import <source> -n <name>` where `<source>` can be a Swagger UI page, site root, or a direct JSON spec. `--openapi/-o` remains supported but optional.
- Auto-discovery: probe Swagger UI config and well-known endpoints to locate the JSON spec.
- Base URL defaults: if the spec lacks `servers[]`, default the workspace `BaseUrl` to the origin of the discovered spec URL.

Directory semantics:

- Import writes a new workspace folder to the current configuration root: `~/.a2c/workspaces/<slug>/` with `workspace.xfer` inside it.
- If the target folder already exists, emit a clear error diagnostic and abort (no overwrite). An explicit `--force` may be considered later but is not part of the default behavior.

Configuration root selection:

- `--config <dir>` points to an alternate configuration directory (root), not a single file. The tool expects `<dir>/config.xfer` and `<dir>/workspaces/` by convention. If absent, they will be created when safe to do so.

## Migration plan

- A non-destructive migration command:
  - Read legacy `workspaces.xfer`.
  - Write each workspace to `~/.a2c/workspaces/<slug>/workspace.xfer` (slug derived from `Name`).
  - Keep `workspaces.xfer` as backup; switch runtime to directory mode.
  - Provide `--dry-run` to preview changes and `--force` to overwrite existing files.

- Backward compatibility:
  - Support loading legacy `workspaces.xfer` alongside directory mode with a deprecation warning.
  - Clear precedence: per-file workspaces override a same-named legacy entry.

## Configuration root and `--config`

- The configuration root is a directory. Default: `~/.a2c/`.
- The CLI option `--config <dir>` overrides the root. Under this root, the loader uses:
  - `<dir>/config.xfer` (optional, global defaults/macros)
  - `<dir>/workspaces/*/workspace.xfer` (required per workspace)
- All read/write operations (import/new/remove/disable/etc.) apply to the selected root.

## CLI/UX additions

- `workspace new -n <name> [-b <baseurl>] [--from <template>]`
- `workspace import <source> -n <name> [-b <baseurl>] [-f]`
- `workspace list` (flags: `--all`, `--disabled`, show file path, mark duplicates)
- `workspace edit -n <name>` (open in $EDITOR)
- `workspace disable/enable -n <name>` (toggle suffix or in-file flag)
- `workspace remove -n <name>` (safe delete with confirmation)
- `reload` continues to rescan the directory and preserve active workspace.

## Validation and collisions

- Validate on load:
  - `Name` present, unique, and matches slug expectations.
  - `BaseUrl` optional but encouraged; warn if blank.
  - Detect cycles if `extend` or includes are used; report with a chain for diagnosis.
- On collision: list conflicting filenames and `Name` values, require user action.

## Performance and caching

- Cache parsed workspaces keyed by file path and mtime/hash.
- Use a filesystem watcher to invalidate and re-parse only changed files.
- Debounce burst events on save (some editors write multiple times).

## Security and secrets

- Keep tokens/secrets in the data store or environment variables; discourage secrets in workspace files.
- Document `.gitignore` patterns (e.g., `~/.a2c/**`), and provide a `workspace pack` that excludes sensitive data by default.

## Future enhancements

- Optional per-workspace folders for large request sets (`requests/`), scripts (`scripts/`), and docs (`README.md`).
- Templates: `~/.a2c/templates/*` to quickly bootstrap new workspaces.
- YAML import support (storage remains XferLang for comments and consistency).
- Workspace packs (zip) including assets; signed packs for distribution.

## Open questions

- Should disabled state be filename-based or an explicit field (or both, with precedence)?
- How strict should slug validation be across platforms (Windows vs. POSIX)?
- Do we support nested directories for organizational grouping (e.g., by team/env)? If so, discovery order?
- Do we allow multiple files contributing to a single logical workspace via includes, or keep one-file-per-workspace as a strong invariant?

## Rollout plan (phased)

1. Implement discovery/read-only support for `~/.a2c/workspaces` in parallel with legacy file; add warnings on collisions.
2. Add `workspace import/new/list/remove/disable/enable/edit` with file-based semantics.
3. Provide the migration command with `--dry-run`; default new features to the directory mode.
4. Make directory mode the default and keep legacy loading behind a compatibility flag; publish migration guidance.

## Acceptance criteria

- Directory mode loads N separate workspace files, surfaces duplicates with actionable errors, and performs comparably to the single-file approach.
- CLI operations work end-to-end without reformatting unrelated files; writes are atomic and resilient to concurrent edits.
- Migration command produces correct output, is reversible (backup legacy file remains), and provides clear logs.

---

Please annotate directly in this document with comments and proposals; we can iterate on decisions (slug rules, disabled semantics, includes, and discovery details) before implementation.

## Packages and version isolation (per-workspace packages)

Problem: If each workspace has its own `packages/` directory, but all code executes in a single process, package version divergence can cause classic assembly binding conflicts ("downgrade" or type identity mismatches).

Recommended mitigations (can be combined):

1) Per-workspace AssemblyLoadContext (ALC)

- Create a custom, collectible `AssemblyLoadContext` per workspace (see `PackageLoadContext.cs`).
- Probe order: workspace `packages/` first, then global/shared locations, then default.
- Cross-boundary contracts: Define narrow interfaces/DTOs in a shared contract assembly loaded in the default context; plugins inside the ALC communicate only via these contracts to avoid type identity leakage.
- Pros: Strong isolation between workspaces; allows different versions to coexist.
- Cons: Anything that must be shared across workspaces must live in the default context; reflection and event wiring require careful marshaling; some libraries assume default context and may resist isolation.

2) Isolated sidecar process per workspace (`--isolated`)

- Spawn a separate process for commands that require conflicting packages; communicate via stdio/JSON-RPC/Named Pipes.
- Pros: Hard isolation; no type identity issues; simplest mental model; OS-level cleanup.
- Cons: Process management overhead; slightly higher latency; more complex debugging.

3) Highest-wins unification (default, conservative) with strict diagnostics

- Resolve packages into a shared load context choosing the highest compatible version across all active workspaces.
- Surface explicit warnings/errors on would-be downgrades; require `--allow-downgrade` to proceed.
- Maintain a `workspace.lock.json` (or `.xfer`) to pin intended versions; on conflict with the unified set, prompt to update or switch to `--isolated`.
- Pros: Simple runtime; good for homogenous environments.
- Cons: Some workspaces may not get the version they requested; runtime surprises if not strictly validated.

4) Shadow copy + binding redirects per workspace

- Restore packages into each workspace, then shadow-copy resolved assemblies into a per-workspace staging folder with rewritten binding policy (where legal) targeting a unified version.
- Pros: Can harmonize minor version skews while keeping workspace ownership.
- Cons: Complexity; not all assemblies tolerate redirects; increased IO.

Operational guidance:

- Default to strategy (3) for simplicity, emit clear diagnostics. Offer `workspace run --isolated` to switch to strategy (2) when conflicts occur.
- For advanced users, enable strategy (1) behind a feature flag, leveraging existing `PackageLoadContext` with clear contract boundaries.
- Provide `workspace packages add|remove|list|restore` commands that operate against `<workspace>/packages/` and maintain a lock file.
- On `reload`, gracefully unload collectible ALCs and dispose sidecars to release file locks and memory.
