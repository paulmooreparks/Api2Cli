# Proposal: Per-workspace files under ~/.a2c/workspaces

Date: 2025-08-21
Status: Draft
Owner: Paul (with collaboration)

## Summary

Move from a single monolithic `~/.a2c/workspaces.xfer` to a directory of per-workspace files under `~/.a2c/workspaces`. Keep global settings in a small, separate file. This improves isolation, preserves formatting/comments, reduces merge conflicts, and enables simpler sharing and templating of workspaces.

## Goals


## Admin tools: export and import (archives)

Goals:

- Create portable archives for an entire configuration root, a set of workspaces, or a single workspace.
- Support public sharing (sanitized) and private backups (full fidelity).

Commands (pack/unpack may be provided as aliases):

- `admin export [--config <dir>] [--workspaces <name1,name2,...>] [--format zip|tar.gz] [--public|--private] -o <output>`
  - Private: include everything under the selected scope except transient caches.
  - Public: exclude secrets/tokens, data store contents, local caches; optionally apply a sanitization policy from `config.xfer`.
  - Include a manifest (e.g., `a2c-manifest.json`) describing version, included workspaces, checksums.

- `admin import [--config <dir>] -i <archive>`
  - Validates manifest, checksums, and target directory emptiness/conflicts.
  - Supports `--workspace <name>` to extract a single workspace from a multi-workspace archive.

Format:

- Default to `.zip` for broad portability; optionally support `.tar.gz`.
- Deterministic ordering and timestamps for reproducible archives.

Sanitization:

- Use explicit allow/deny lists in `config.xfer` (glob patterns) for both files and fields.
- Provide `--dry-run` to preview what will be included/excluded.

## Repositories of workspaces and configurations

Concepts:

- Sources: named repositories that the tool can query for indexes and fetch archives from.
- Types:
  - Filesystem source: a local directory containing an index and archives.
  - Git source: a Git URL; the tool clones/fetches and reads an index file and archives from known paths.
  - HTTP source: an HTTPS endpoint serving an index JSON and downloadable archives (static site or simple API).

Index:

- `index.json` (or `.xfer`) with entries: name, version, description, tags, checksum, archive URL, signature (optional), size.
- Support channels (stable/preview) and simple semver for updates.

Security and trust:

- Verify checksums; optionally verify signatures (Sigstore, minisign, or similar).
- For private HTTP/Git sources, support Basic/Bearer auth via env vars or OS credential manager.

CLI:

- `repo add <name> <url> [--type fs|git|http]`
- `repo list`, `repo remove <name>`
- `workspace search <query> [--repo <name>]`
- `workspace install <name>[@version] [--repo <name>]` (downloads and unpacks into the current config root)
- `workspace update <name>` (checks installed vs index)

Caching:

- Maintain a local cache of downloaded archives with checksum keys; reuse on repeated installs.

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

Minimal (required):

- `~/.a2c/workspaces/<slug>/workspace.xfer` — primary workspace definition.

Optional (by convention, not enforced):

- Authors may add subdirectories such as `scripts/`, `requests/`, `docs/`, etc.
- The tool does not auto-include files from these folders; reference any additional files explicitly from `workspace.xfer` to control load order.
- If per-workspace packages are adopted, the conventional location is `~/.a2c/workspaces/<slug>/packages/` (see Packages and version isolation).

## Discovery and precedence

- On startup or `reload`:
  1. Load `~/.a2c/config.xfer` if present (global defaults/macros).
  2. Enumerate `~/.a2c/workspaces/*/workspace.xfer` (each subdirectory with a `workspace.xfer` is a workspace).
  3. Ignore temp/backup/hidden artifacts by default (`*~`, `*.bak`, `.swp`, files starting with `.`), unless an advanced flag is set.
  4. Parse each workspace; build a map by `Name`.
  5. On name collisions, fail fast with a clear error listing the filenames and the duplicate `Name` values.

- Disabled workspaces: support either a filename suffix (e.g., `foo.xfer.disabled`) or an `enabled: false` flag in the file. The suffix approach avoids parsing disabled files and is more obvious in the filesystem.

Lazy initialization:

- Startup performs only enumeration and parsing of `workspace.xfer` to power help/UX.
- No package restore/loading, script execution, or network operations occur until the user switches to a workspace.

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
- If the target folder already exists, emit a clear error diagnostic and abort (no overwrite).

Configuration root selection:

- `--config <dir>` points to an alternate configuration directory (root), not a single file. The tool expects `<dir>/config.xfer` and `<dir>/workspaces/` by convention. If absent, they will be created when safe to do so.

## Data store and environment files

Locations:

- Top-level (shared defaults):
  - Env file: `<configRoot>/.env`
  - Data store: `<configRoot>/data/a2c.db` (Xfer string-only)
- Per-workspace (overrides):
  - Env file: `<workspace>/.env`
  - Data store: `<workspace>/data.db` (or `<workspace>/data/a2c.db`)

Precedence and merge:

- Load `<configRoot>/.env` first, then overlay `<workspace>/.env` (later wins for duplicate keys). Support `--no-root-env` to opt out of root env for a workspace session.
- Data store resolution order for a workspace:
  1) Explicit path in `workspace.xfer` (e.g., `dataStore.path`)
  2) `<workspace>/data.db`
  3) `<configRoot>/data/a2c.db`
- Allow `A2C_DATASTORE` env var to override for one-off sessions.

Security guidance:

- Keep secrets out of Xfer config; prefer `.env` and OS secret stores. Ensure `.env` and `data/` are excluded from public exports unless `--private` is specified.
- Provide redaction rules for `admin export --public` (e.g., `TOKEN=*redacted*`).

CLI helpers:

- `workspace env get|set|unset -n <name> <key> [<value>]` — manipulate `<workspace>/.env` safely (atomic writes).
- `workspace datastore path -n <name>` — show the resolved store path; `set` to update `workspace.xfer` or sidecar.
- `workspace datastore backup|compact -n <name> [-o <path>]` — maintenance operations on the per-workspace store.

Notes:

- Lazy init applies: `.env` is read and the data store is opened only when entering a workspace, not at startup.

## Granting write access outside the repo workspace (operational)

This assistant can only write within folders that are part of the current VS Code workspace. To let me help restructure files under locations like `~/.a2c/` while keeping sensitive data local:

Options:

1) Multi-root workspace (recommended)
  - In VS Code: add your target folder (e.g., `C:\Users\paul\.a2c`) as an additional folder to the workspace.
  - I can then create/edit files directly under that folder without touching the repo.

2) Stage a copy
  - Create a staging directory (e.g., `C:\Users\paul\a2c-staging`) and copy/sanitize `workspaces.xfer` into it.
  - Add the staging folder to the workspace; I’ll split it safely there. You can review and then replace the original yourself.

3) Symlink/junction
  - Create a junction inside the repo that points to your target config root; add that path to the workspace. Use with care on Windows.

4) Use export/import once available
  - Run `admin export --private -o <archive>` from the source machine, then add an empty folder to the workspace and have me `admin import -i <archive>` there to materialize the structure.

Whichever route you choose, you stay in control: I won’t access or expose content unless you add the folder into the workspace.

## Composed workspaces (multi-file, versioned layers)

Goal: Avoid destructive round-trips by composing an effective workspace from multiple files (layers), where human-authored base files remain stable and machine- or team-provided overlays can evolve independently and be versioned.

Key concepts:

- Root descriptor: `workspace.xfer` remains the entry point and defines the workspace `Name`, `BaseUrl`, and composition order.
- Layers: one or more additional files/folders referenced from `workspace.xfer` in a defined order (base-first, overlay-last). Examples:
  - `layers/core.xfer` (human-authored)
  - `layers/env.prod.xfer` (environment overrides)
  - `generated/openapi.xfer` (machine-generated from spec)
- Precedence: later layers override or extend earlier ones. For lists (requests, scripts), default behavior is append; for maps (variables/settings), later keys override earlier keys.
- Isolation: machine-generated content should live under `generated/` and be the only files touched by update flows.

Loader semantics:

- Parse-only on startup; no script execution or network. Resolve includes and validate composition statically (missing files, cycles).
- Evaluate layers in order to construct the effective workspace model used at runtime when the user switches into the workspace.
- Detect and report conflicts (e.g., duplicate request IDs with incompatible shapes) with actionable file/line origins.

Versioning and locking:

- Each layer may declare `version:` (semver) and `id:` metadata at the top. Example:
  - `// layer: env.prod, version: 1.2.0`
- Maintain `workspace.lock.xfer` (or `.json`) recording exact layer versions, checksums, and paths.
- On update (e.g., spec regeneration), update only the generated layer and refresh the lockfile.
- Support rollback by keeping timestamped copies in `.history/` or via Git.

Structure example (conventional, not enforced):

- `workspace.xfer` (root)
- `layers/` (human/team layers)
- `generated/` (tool-managed layers)

CLI for layers:

- `workspace layers list -n <name>` — show effective order with source paths and versions.
- `workspace layers add -n <name> <path> [--before <layer>|--after <layer>]` — register a layer in composition.
- `workspace layers remove -n <name> <layer>` — unregister a layer.
- `workspace freeze -n <name>` — write/update `workspace.lock.*` with exact versions/checksums.
- `workspace thaw -n <name>` — allow layer updates not pinned by lock.

Notes:

- This composes cleanly with Spec-driven updates: the OpenAPI output is just another layer (`generated/openapi.xfer`).
- Because the root file references layers explicitly, users can keep arbitrary folder layouts without the tool imposing auto-include rules.

## Spec-driven updates (preserving comments and formatting)

Goal: Allow updating a workspace from a published API spec (e.g., OpenAPI) while minimizing edits to human-authored files and preserving formatting/comments.

Core ideas:

- Provenance sidecar: store spec source, hash, timestamp, and mapping in a sidecar (e.g., `workspace.provenance.json|.xfer`) to avoid touching `workspace.xfer` on updates.
- Generated artifacts segregation: generate machine-authored requests/scripts under a conventional subfolder (e.g., `generated/` or `requests/generated/`) referenced explicitly by `workspace.xfer`. Updates only rewrite files under this folder.
- Minimal-diff writing: for any regenerated file, perform an AST-aware or semantic diff to produce minimal text changes; fall back to full rewrite within generated areas.
- Dry-run and diff: provide `--dry-run` and diff views before applying changes; take a rollback snapshot.

Options considered:

1) Overlay file (recommended)
  - Keep human-authored `workspace.xfer` untouched; store generated requests and metadata in an overlay folder (e.g., `generated/`).
  - Merge overlay at load time. Only overlay changes on update.
  - Pros: No round-tripping of main file; comments preserved.
  - Cons: Requires explicit include/reference to overlay; complexity in merge rules.

2) Region markers in `workspace.xfer`
  - Generated sections delimited by markers; updates replace only inside markers.
  - Pros: Single file remains central.
  - Cons: Marker management; risk of user edits inside regions.

3) Lossless (trivia-preserving) serializer for XferLang (deferred)
  - Extend XferLang to capture comments/whitespace as trivia and re-emit unchanged tokens.
  - Pros: Precise edits anywhere.
  - Cons: Significant engineering effort; complexity not justified initially.

Recommendation: Use the overlay + sidecar approach first. Add region markers as an optional mode for users who prefer single-file layouts. Revisit a trivia-preserving serializer only if necessary.

Provenance details:

- Sidecar fields: `sourceUri`, `etag`/`lastModified` (if HTTP), `specHash`, generator version, generated folder path, list of operations → file mapping.
- On update: fetch/provide new spec, compare hash/ETag, show summary of changes (added/removed/changed operations), then regenerate overlay files only.
- Respect pinned items: allow users to mark certain generated items as “pinned” to prevent overwrite; write pins to sidecar.

CLI:

- `workspace check-spec -n <name> [--source <uri|file>]` — compare current spec provenance with remote/local spec; no writes.
- `workspace diff-spec -n <name> [--source <uri|file>]` — produce human-readable diff of impacted items.
- `workspace update-from-spec -n <name> [--source <uri|file>] [--generated-dir <path>] [--dry-run] [--backup]` — regenerate overlay, minimal writes, snapshot before changes.
- `workspace pin -n <name> <operationId>` and `workspace unpin ...` — protect/unprotect specific generated items.

Edge cases:

- Spec removes operations: move corresponding generated files to a `generated/_removed/` quarantine or delete with confirmation.
- Servers/base URL changes: require `--accept-baseurl-change` or prompt; otherwise keep existing `BaseUrl`.
- YAML specs: supported when YAML parsing is enabled; otherwise guide to convert to JSON.

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
- `admin export` / `admin import` for archiving/restoring configurations or specific workspaces.
- `workspace check-spec`, `workspace diff-spec`, `workspace update-from-spec`, and `workspace pin/unpin` for spec-driven maintenance.
- `workspace layers list|add|remove`, `workspace freeze|thaw` for composition and version control.

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
- Document `.gitignore` patterns (e.g., `~/.a2c/**`), and provide an `admin export` that excludes sensitive data by default.

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
