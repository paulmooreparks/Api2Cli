# Proposal: Per-workspace files under ~/.a2c/workspaces

Date: 2025-08-21
Status: Draft
Owner: Paul (with collaboration)

## Summary

Move from a single monolithic `~/.a2c/workspaces.xfer` to a directory of per-workspace files under `~/.a2c/workspaces`. Keep global settings in a small, separate file. This improves isolation, preserves formatting/comments, reduces merge conflicts, and enables simpler sharing and templating of workspaces.

## Goals

- Create portable archives for an entire configuration root, a set of workspaces, or a single workspace.
- Support public sharing (sanitized) and private backups (full fidelity).

## Non-goals (for initial phase)

- Changing the internal XferLang representation beyond what’s needed for file splitting.
- Introducing YAML-as-storage (YAML import can remain a possible future feature).
- Deep refactors of scripting/runtime; this is primarily configuration layout and IO semantics.

## Priority of Implementation

1. Proposed layout
2. Slug rules
3. Workspace folder structure
   1. Skip optional
4.

## Proposed layout

- Global config (small):
  - `~/.a2c/config.xfer` (global macros, scripts, feature flags, defaults, paths)
- Workspaces (directory-per-workspace by default):
  - `~/.a2c/workspaces/<slug>/workspace.xfer` (required)

Rationale: Directory-per-workspace enables clean separation of assets, scripts, request collections, and optional per-workspace packages. For the initial phase, each workspace uses a single `workspace.xfer` file; splitting across multiple files ("layers") is deferred. Workspace identity is the folder name (case-insensitive); no `name` key is required or used in `workspace.xfer`.

### Slug rules (and case-insensitivity)

- Filename slug should be stable, human-friendly, and filesystem-safe.
- Suggested: lowercase, ASCII, dash-separated; strip/replace spaces and punctuation.
- Example: `"My API (Prod)" → my-api-prod/` (folder) containing `workspace.xfer`.
- Identity is the folder name; the loader treats workspace names case-insensitively (e.g., `INT`, `Int`, and `int` refer to the same workspace). Prefer lowercase to avoid ambiguity.
- Use the `description` property inside `workspace.xfer` for a human-friendly label; do not include a `name` field.

### Workspace folder structure

Minimal (required):

- `~/.a2c/workspaces/<slug>/workspace.xfer` — primary workspace definition.
- If per-workspace packages are installed, the reserved location is `<workspace>/.packages/`; a global cache can live at `~/.a2c/.packages/` (see Packages and version isolation).

Optional (by convention, not enforced):

- Authors may add subdirectories such as `scripts/`, `requests/`, `docs/`, etc., for organization only.
- At this stage, the loader does not load files from these folders. Splitting across files is deferred; all configuration remains in `workspace.xfer`.

### Organizing large workspaces

- Prefer non-dot folders for user-authored content you’ll browse/edit: `requests/`, `scripts/`, `assets/`, `docs/`.
- Use dot-prefixed folders for tool-managed internals to avoid collisions/noise: `.packages/`, `.history/`, `.locks/`.
- `generated/` may remain non-dot for transparency; use `.generated/` if you want it clearly tool-owned. The loader never auto-includes; reference explicitly from `workspace.xfer` (or via a layer) to control order/scope.

## Discovery and precedence

- On startup or `reload`:
  1. Load `~/.a2c/config.xfer` if present (global defaults/macros).
  2. Enumerate `~/.a2c/workspaces/*/workspace.xfer` (each subdirectory with a `workspace.xfer` is a workspace).
  3. Ignore temp/backup/hidden artifacts by default (`*~`, `*.bak`, `.swp`, files starting with `.`), unless an advanced flag is set.
  4. Parse each workspace; build a map by folder slug (case-insensitive).
  5. On collisions (two folders differing only by case, or duplicate symlinked paths), fail fast with a clear error listing the conflicting folder names/paths.

Temporarily ignore specific workspaces:

- Startup option: `--ignore <slug>` may be provided multiple times (e.g., `a2c --ignore foo --ignore bar`) to skip specific workspaces during discovery. Ignored workspaces are not parsed or activated and are omitted from listings.
- Matching is by folder slug, case-insensitive. Unknown names are warned and otherwise ignored.
- Intended use: temporarily bypass a collision or a broken workspace until it can be fixed or renamed.

Lazy initialization:

- Startup performs only enumeration and parsing of `workspace.xfer` to power help/UX.
- No package restore/loading, script execution, or network operations occur until the user switches to a workspace.

## Workspace inheritance (extend) and abstract workspaces

- A workspace may inherit from another using `extend "<name>"`.
- Mark a base (abstract) workspace with `isHidden ~true` so it doesn’t appear in default lists/prompts but can be inherited.
- Example:
  - `abacus` (abstract, hidden) defines common scripts/requests/properties.
  - `dev`, `int`, `uat`, `prod` extend `abacus` and set env-specific settings (e.g., `baseUrl`, `tokenName`).
  - `devint`, `devuat`, `devprod` extend `dev`; multi-level inheritance is supported.
- Merge semantics: evaluate base-first then overlay child; for maps, later keys override; for lists, default is append. See also “Composed workspaces”.
- Validation: detect cycles and fail with a clear inheritance chain; hidden workspaces are excluded from `workspace list` unless `--all` is provided.

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
 - Allow a one-off CLI override via `--datastore <path>`.

Security guidance:

- Keep secrets out of Xfer config; prefer `.env` and OS secret stores. Ensure `.env` and `data/` are excluded from public exports unless `--private` is specified.
- Provide redaction rules for `admin export --public` (e.g., `TOKEN=*redacted*`).

CLI helpers:

- `workspace env get|set|unset -n <name> <key> [<value>]` — manipulate `<workspace>/.env` safely (atomic writes).
- `workspace datastore path -n <name>` — show the resolved store path; `set` to update `workspace.xfer` or sidecar.
- `workspace datastore backup|compact -n <name> [-o <path>]` — maintenance operations on the per-workspace store.

Notes:

- Lazy init applies: `.env` is read and the data store is opened only when entering a workspace, not at startup.

## Composed workspaces (deferred)

Deferred for initial phase to reduce complexity. All workspace configuration remains in a single `workspace.xfer` file. We can reintroduce multi-file composition in a later iteration when needed.

## Workspace folders (hierarchical grouping for large workspaces)

Problem: Large workspaces benefit from Postman-like folders to organize requests and scripts by service/feature.

Design (logical folders):

- Allow nested maps under `requests {}` and `scripts {}` to represent folders. Example:

  ```xferlang
  requests {
      config {
          get_SystemSetting { /* ... */ }
      }
      bundle {
          get_BundleType { /* ... */ }
      }
  }
  scripts {
      admin {
          login { /* ... */ }
      }
  }
  ```

- Effective IDs are the path of keys joined by dots (no slashes), e.g., `config.get_SystemSetting`.
  - Avoid `/` in names to prevent clashes with REPL navigation. Use dot `.` as the canonical separator.
- Back-compat: flat items continue to work; grouping is optional and additive.

CLI/REPL behavior:

- Generate nested subcommands mirroring folders where practical, or expose flat names with dot-qualified IDs.
- In JS, access via the same dotted path: `workspace.config.get_SystemSetting.execute(...)`.

File organization (optional, not auto-included):

- Keep user-authored content readable:
  - `requests/` and `scripts/` may be split into multiple Xfer files: e.g., `requests/config.xfer`, `requests/bundle.xfer`.
  - Reference these files via layers from `workspace.xfer`. Each file contributes to the same logical tree (e.g., `requests { config { ... } }`).
- Tool-managed internals continue to use dot folders: `.packages/`, `.history/`, `.locks/`. Generated content may use `generated/` (or `.generated/` if preferred) and is referenced explicitly.

Merging and overrides:

- Composition follows existing rules: base-first then overlays; nested paths merge; later definitions override earlier ones at the same fully-qualified ID.
- Inheritance via `extend` applies the same: child folders add/override parent folders by fully-qualified ID.

Validation:

- Disallow duplicate fully-qualified IDs across layers/files.
- Detect ambiguous folder vs item name collisions (e.g., `config` item vs `config` folder) and fail with a clear message.

### Splitting folders into files and hierarchical includes

There are two supported ways to factor large request/script trees across files:

1) Whole-file layers that contribute at the top level

- Each file contains its own `requests { ... }` and/or `scripts { ... }` blocks with nested folders as needed.
- Reference files from `layers { ... }` in a defined order (base-first, overlays-last). Example:

  ```xferlang
  layers {
      core file 'layers/core.xfer'                 // requests { config { ... } }
      requests_bundle file 'requests/bundle.xfer'  // requests { bundle { ... } }
      scripts_admin file 'scripts/admin.xfer'      // scripts { admin { ... } }
  }
  ```

2) Subtree mounts using dotted-path keys

- Mount a file directly at a nested path using a dotted key under `layers {}`. The included file’s root is treated as the map content for that node.
- This avoids repeating the `requests { folder { ... } }` wrapper inside the file.

  ```xferlang
  layers {
      requests.config file 'requests/config.xfer'    // mounts at requests → config
      requests.bundle file 'requests/bundle.xfer'    // mounts at requests → bundle
      scripts.admin  file 'scripts/admin.xfer'       // mounts at scripts  → admin
  }
  ```

  Where `requests/config.xfer` can simply contain the child items:

  ```xferlang
  {
      get_SystemSetting { /* ... */ }
      get_Rail { /* ... */ }
  }
  ```

Ordering and merge rules

- Files are evaluated in `layers {}` order: earlier entries compose first; later ones overlay/append.
- Subtree mounts create intermediate maps as needed; mounting into a non-map node is an error.
- For maps, later keys override earlier keys at the same fully-qualified path; for lists, later files append.

Validation

- Disallow duplicate fully-qualified IDs across all sources (base + layers + mounts).
- Fail on folder-vs-item conflicts at the same path.
- Fail if any dotted-path mount targets a non-map node.

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
  - Write each workspace to `~/.a2c/workspaces/<slug>/workspace.xfer` (slug derived from the legacy `Name`).
  - Keep `workspaces.xfer` as backup; switch runtime to directory mode.
  - Provide `--dry-run` to preview changes and `--force` to overwrite existing files.

- Output semantics:
  - Omit any `name` field in the generated `workspace.xfer`; rely on the folder name for identity.
  - If a legacy workspace lacks a clear name, derive a slug from its index or prompt for one.

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

- `workspace new -n <name> [-b <baseurl>] [--from <template>]` (creates an empty workspace scaffold by default)
- `workspace import <source> -n <name> [-b <baseurl>] [-f]`
- `workspace list` (flags: `--all`, `--disabled`, show file path, mark duplicates)
- `workspace edit -n <name>` (open in $EDITOR)
- `workspace disable/enable -n <name>` (toggle suffix or in-file flag)
- `workspace remove -n <name>` (safe delete with confirmation)
- `reload` continues to rescan the directory and preserve active workspace.
- `admin export` / `admin import` for archiving/restoring configurations or specific workspaces.
- `workspace check-spec`, `workspace diff-spec`, `workspace update-from-spec`, and `workspace pin/unpin` for spec-driven maintenance.


## Validation and collisions

- Validate on load:
  - Folder slug is valid (recommended lowercase, ASCII, dash-separated) and unique when compared case-insensitively.
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

## Implementation plan (end-to-end)

This plan sequences the whole design from MVP to v1.0, with concrete deliverables, acceptance checks, and noted risks. Phases can overlap where noted; prefer frequent, small merges behind flags.

Phase 0 — Runtime hardening and JSON removal

- Remove Newtonsoft.Json from data paths; keep Xfer-only store and native JS in ClearScript.
- Orchestrator: lazy activation, base-first chaining, strict error surfacing; timing via CLI switches (e.g., `--timings`); structured logs.
- Tests: error propagation, cancellation/timeout, and timing option toggles.
- Exit criteria: existing commands unaffected; tests green on Windows; no Newtonsoft.Json in runtime deps.

Phase 1 — Config root + discovery (read-only)

- Implement `--config <dir>` selection and default `~/.a2c/` root.
- Enumerate `workspaces/*/workspace.xfer`; case-insensitive slug map; collision errors.
- Implement repeatable `--ignore <slug>` for startup and `reload`.
- File watcher + cache with debounced invalidation.
- CLI: `workspace list [--all] [--path]` (hidden excluded unless `--all`).
- Exit criteria: discovery honors ignore; collisions reported with paths; list is accurate and fast (<200ms for 50 workspaces, cached).

Phase 2 — REPL + typed CLI foundation

- Adopt typed binding (Cliffer). Stabilize root/workspace REPL contexts.
- Navigation: `"/"` pops to root, `".."` pops one level; recursion guards to avoid nesting loops.
- Commands: `reload`, `workspace use <name>` to enter REPL, show current workspace in prompt.
- Exit criteria: interactive flows stable; no accidental deep recursion; help text generated from types.

Phase 3 — Workspace CRUD (single-file per workspace)

- `workspace new -n <name> [-b <baseurl>] [--from <template>]` — scaffold folder + `workspace.xfer`.
- `workspace import <source> -n <name> [-b <baseurl>]` — create folder; no overwrite unless `-f`.
- `workspace edit/remove/disable/enable` — atomic writes; suffix or in-file flag for disable (as decided).
- Slug derivation rules; path conflicts handled with clear diagnostics.
- Exit criteria: CRUD operations are atomic, idempotent; formatting preserved for unrelated lines.

Phase 4 — Data store and environment

- Resolve `.env` precedence (root then workspace) and a `--datastore <path>` override.
- Open data store lazily on workspace entry; Xfer string-only store.
- CLI: `workspace env get|set|unset`, `workspace datastore path|set|backup|compact`.
- Exit criteria: env merges and store resolution match docs; maintenance ops succeed with backups.

Phase 5 — Inheritance and validation

- Implement `extend` with base-first merge; maps override, lists append by default.
- `isHidden ~true` respected in listings; `--all` shows abstract workspaces.
- Detect inheritance cycles; helpful chain in errors. Validate dotted IDs and collisions.
- Exit criteria: multi-level dev/int/uat/prod scenario works; cycles caught with actionable errors.

Phase 6 — Packages and version isolation

- Default: highest-wins unification with strict diagnostics and a `workspace.lock.xfer`.
- Advanced: per-workspace `AssemblyLoadContext` behind a flag; clean unload on `reload`.
- Escape hatch: `workspace run --isolated` launches a sidecar process when conflicts arise.
- CLI: `workspace packages add|remove|list|restore` operating in `<workspace>/.packages/`.
- Exit criteria: conflicting versions are either unified with warnings or runnable via `--isolated`; no leaked file locks after reload.

Phase 7 — Admin export/import

- `admin export`/`admin import` with zip archives, deterministic manifests, and sanitization policy.
- Public/private modes; `--dry-run` diff of included files and redactions.
- Exit criteria: round-trip exports import cleanly; public export contains no secrets by default.

Phase 8 — Repositories of workspaces

- Sources: fs/git/http with `repo add/list/remove`.
- `workspace search/install/update` with checksum verification and local caching.
- Optional signature verification for secure sources.
- Exit criteria: install from all three source types; updates respect channels and checksums.

Phase 9 — Spec-driven updates

- Provenance sidecar; generated overlay directory; minimal-diff writes inside generated areas.
- Commands: `workspace check-spec`, `diff-spec`, `update-from-spec [--dry-run]`, `pin/unpin`.
- Exit criteria: regenerate from updated OpenAPI without touching human-authored sections; diff is clear and scoped.

Phase 10 — Migration + defaults flip

- `admin migrate workspaces` from legacy monolith to per-folder structure; `--dry-run` and backup.
- Default runtime to directory mode; legacy path behind compatibility flag with deprecation notice.
- Exit criteria: migration succeeds on real configs; startup prefers directory mode.

Cross-cutting quality gates

- Build, lint, unit + integration tests blocking; small smoke tests on Windows.
- Performance budgets for discovery (<200ms cached), list (<100ms cached), REPL latency (<30ms per prompt render).
- Robust error messages with source paths; telemetry/logs gated by explicit CLI switches (e.g., `--telemetry`, `--verbose`); cancellation support for long operations.

Risks and mitigations

- Case sensitivity differences (Windows vs POSIX): enforce case-insensitive slug uniqueness; normalize everywhere.
- ClearScript recursion or leaks: recursion guards, deterministic disposal on REPL exit; add leak tests.
- ALC unload reliability: use collectible ALCs; ensure no static roots; add retries on file locks.
- Archive sanitization gaps: default-deny for secrets; explicit allow/deny lists; `--dry-run` reviews.
- Sidecar process management: timeouts, health checks, and graceful shutdown on `reload`.

Dependencies and parallelism

- Phases 1–3 are gating for most UX; 4–5 can proceed in parallel once the workspace loader is stable.
- Export/import (7) and repos (8) can start after 3; spec updates (9) can start after 3 with a simple overlay generator.

Tracking

- Create GitHub milestones for each phase; track issues under labels: `workspace`, `cli`, `orchestration`, `packages`, `export`, `repos`, `spec-update`.
- Maintain a living checklist in `README.md` or this whitepaper, updating acceptance per phase as we ship.

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

## Admin tools: export and import (archives)

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
- For private HTTP/Git sources, support Basic/Bearer auth via dedicated CLI options (e.g., `--auth-token`, `--auth-user/--auth-pass`) or the OS credential manager.

CLI:

- `repo add <name> <url> [--type fs|git|http]`
- `repo list`, `repo remove <name>`
- `workspace search <query> [--repo <name>]`
- `workspace install <name>[@version] [--repo <name>]` (downloads and unpacks into the current config root)
- `workspace update <name>` (checks installed vs index)

Caching:

- Maintain a local cache of downloaded archives with checksum keys; reuse on repeated installs.

## Future work: multi-file composition (layers)

Deferred for now to keep the MVP simple. When reintroduced, we’ll:

- Allow `workspace.xfer` to reference additional files in an explicit order (base-first, overlay-last).
- Support both whole-file contributions (containing `requests {}` / `scripts {}`) and dotted-path subtree mounts (e.g., `requests.config file 'requests/config.xfer'`).
- Keep strict, deterministic merge rules: maps override by key; lists append; conflicts error with source locations.
- Maintain a lock/provenance sidecar only for generated overlays (optional), keeping human-authored files untouched.
- Provide `workspace layers list` for introspection and treat layers as opt-in per workspace.
- Offer a no-op migration for existing single-file workspaces; layers are additive, not required.

