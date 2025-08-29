# A2C MCP Server, Async Jobs, and Multi-Pane UI Design Whitepaper

Version: 0.1 (Draft)
Date: 2025-08-26
Author: (auto-generated from design discussion)
Status: Proposal (targeting phased implementation)

## 1. Purpose
Provide a structured, low-risk path to:
1. Expose A2C capabilities to LLM agents/tools via an explicit MCP (Model Context Protocol) server started only in REPL mode when requested.
2. Introduce an asynchronous job framework so long-running scripts/requests do not block interactive use.
3. Add real-time status visibility (status bar) in the existing console and later an optional richer TUI with multiple panes.
4. Preserve backward compatibility and isolate new complexity in discrete, testable modules.

## 2. Scope (Phase-Oriented)
In-scope (initial and near-term):
- Explicit REPL-only MCP server (opt-in flag).
- Async job queue for scripts/requests (single engine serialization first).
- CLI commands: async, jobs, job <id>, cancel, (optional wait), status bar rendering.
- Internal event model for job lifecycle & workspace activation.
- MCP tools mirroring core REPL features.
- Minimal security (local user, optional token later).

Out-of-scope (initial):
- Full multi-engine concurrent script execution.
- Persistent job history across process restarts.
- Comprehensive access control lists (ACLs).
- Rich multi-pane TUI (deferred to later phases).

## 3. Phase Roadmap
| Phase | Goals | Deliverables |
|-------|-------|--------------|
| 1 | Foundations: JobManager + MCP skeleton + async commands + status bar (basic) | JobManager, async & jobs commands, status counters, MCP server (listWorkspaces, script.run sync/async), docs |
| 2 | Robustness: cancellation, logs, job detail, MCP job APIs + streaming | cancel, job logs, job.<id>, jobs.cancel/get/list, notifications |
| 3 | TUI introduction (optional --tui) + workspace panes skeleton | IReplUi abstraction, Terminal.Gui prototype |
| 4 | Advanced TUI: multi-pane enhancements, job detail pane, settings, themes | Pane manager, focus switching, layout persistence |
| 5 | Optimization: multi-engine parallelism, persistence, security token | Configurable engine pools, job state persistence, auth token |

## 4. MCP Server (Explicit REPL Mode)
Invocation: `a2c repl --mcp [--mcp-port 0]`
- Default transport: TCP loopback (port 0 for ephemeral assignment).
- Future: Named pipe / Unix domain socket primary; TCP fallback.
- Logs startup line: `MCP listening on 127.0.0.1:54123 (session XXX)`.
- 1:1 mapping between REPL session and MCP session (Phase 1).
- Shutdown: REPL exit triggers server stop; send `server.exiting` notification.

Capabilities payload example:
```json
{
  "apiVersion": "0.1",
  "replSessionId": "sess_ulid",
  "tools": ["workspaces.list","workspace.activate","script.run","jobs.list","jobs.get","jobs.cancel"]
}
```

### Initial MCP Tools (Phase 1)
- workspaces.list -> { workspaces: [...] }
- workspace.activate { name }
- script.run { workspace, name, args?, async? } -> { result } or { jobId }
- request.run (same pattern; optional in Phase 1 if simple)
- jobs.list -> { jobs: [...] }
- jobs.get { id } -> full job object
(Phase 2 adds jobs.cancel, streaming notifications)

### Transport & Discovery
No global daemon in Phase 1; MCP bound only to active REPL process. Future daemon design (shared per-user) can reuse the same handlers.

## 5. Async Job Framework
### Goals
- Non-blocking script/request execution.
- Simple, deterministic ordering (serialize through single ClearScript engine initially).
- Extensible to parallelism later.

### Core Types
```csharp
enum JobStatus { Queued, Running, Succeeded, Failed, Cancelled }

record Job {
  string Id;                 // Ulid or short unique
  string Type;               // "script" | "request" | future: "import", "process"
  string Workspace;
  string Target;             // script or request name
  object?[] Args;            // Positional args
  JobStatus Status;
  DateTimeOffset SubmittedAt;
  DateTimeOffset? StartedAt;
  DateTimeOffset? EndedAt;
  string? Error;             // message
  string? ResultPreview;     // truncated result
  ConcurrentQueue<string> Log; // bounded ring
  CancellationTokenSource Cts;
}
```

### Manager Responsibilities
- Queue / dequeue jobs (Channel<Job> or ActionBlock).
- Assign sequential execution (Phase 1).
- Update status + emit events (Started, Completed, Failed, Cancelled).
- Purge oldest completed jobs past N (e.g. 200).

### Execution Flow
1. User issues `async runall`.
2. Parser resolves workspace + script -> Job created (Queued).
3. Worker loop dequeues, sets StartedAt, sets active workspace, runs via existing invocation path.
4. Capture result preview (string truncation, e.g. 256 chars). On error record Error.
5. Emit Completed/Failed event, update EndedAt.

### Cancellation
- Phase 1: stub method returns not supported or queued-only cancellation.
- Phase 2: propagate token to HTTP calls and process.runCommand wrappers; mark Cancelled when cooperative.

### Logging Capture
Wrap `IConsoleWriter` in a job-aware decorator (ambient currentJobId via AsyncLocal) capturing lines to a bounded queue (e.g. max 200 lines). Provide `job <id>` to view.

## 6. REPL Commands (Proposed Syntax)
| Command | Description |
|---------|-------------|
| async <script> [args] | Queue script job |
| async request <requestName> [payload?] | Queue request job |
| jobs | List jobs (ID, Type, Wksp, Target, Status, Dur) |
| job <id> | Detailed job info + last N log lines |
| cancel <id> | Request cancellation (Phase 2) |
| wait <id> | Block until completion (optional) |
| view status on|off | Toggle status bar |
| status | One-shot status snapshot (if bar off) |

### Output Guidelines
- Use resource-based localization keys for messages to stay consistent.
- Structured logging event code namespace: `jobs.queue`, `jobs.start`, `jobs.fail`.

## 7. Status Bar (Phase 1 Minimal)
Placement: Last console line.
Content example:
`[Jobs: running=1 queued=2 done=5 failed=1 last=jb_1A2B3 (runall)]`
Refresh triggers:
- After each REPL command.
- On Job event (coalesce high-frequency updates with debounce, e.g. 100ms).
Implementation: store previous string; redraw only on change. Use `Console.SetCursorPosition` and clear tail.
Fallback: If output height < 5 lines or redirected, disable automatically.

## 8. Event Model
Events (record structs):
- JobQueued(Job)
- JobStarted(Job)
- JobCompleted(Job)
- JobFailed(Job)
- JobCancelled(Job)
- WorkspaceActivated(name)
- McpServerStarted(endpoint)
- McpServerStopped

Dispatcher: lightweight in-process pub/sub (thread-safe list of handlers). Status bar subscribes; MCP (Phase 2) relays selected events as notifications.

## 9. TUI (Future Phases)
Flag: `a2c repl --tui` (can combine with --mcp). Fallback to plain if unsupported.

### Pane Concepts
- Workspace List (tree) left.
- Main Output Pane (active workspace + REPL transcript).
- Jobs Pane (live table) right.
- Input Line bottom.
- Optional Job Detail Pane (modal or lower split) when selecting a job.

### UI Abstraction
`IReplUi`:
```csharp
interface IReplUi {
  void WriteOutput(string text, string? channel = null);
  void UpdateStatus(StatusSnapshot snapshot);
  void DisplayJob(Job job);
  string ReadCommand();
  void FocusWorkspace(string name);
}
```
Plain console + TUI implementations; REPL loop depends only on interface.

## 10. Security & Isolation
Phase 1 (local): trust boundary = current user.
- No network exposure beyond loopback.
- Optional env flag to restrict tools: `A2C_MCP_TOOLS=workspaces.list,script.run`.

Future hardening:
- Per-command allow list in config.
- Capability token file with 0600 permissions or Windows ACL.
- Rate limiting (import, process.runCommand) if needed.

## 11. Versioning & Compatibility
Expose in capabilities:
```json
{ "apiVersion": "0.1", "implVersion": "<semver>" }
```
Rules:
- Clients MAY warn on differing minor; MUST reject on major mismatch.
- Breaking changes bump major of apiVersion.

## 12. Data Contracts (Initial)
Job (public view):
```json
{
  "id": "jb_01HJ...",
  "type": "script",
  "workspace": "devint",
  "target": "runall",
  "status": "Running",
  "submittedAt": "2025-08-26T12:34:56Z",
  "startedAt": "2025-08-26T12:34:57Z",
  "endedAt": null,
  "durationMs": 1234,
  "resultPreview": null,
  "error": null
}
```
(MCP may request verbose=true to include logs.)

## 13. Edge Cases & Mitigations
| Case | Handling |
|------|----------|
| Engine busy when async queued | Job sits Queued; status bar shows queued count. |
| Cancellation of non-cooperative script | Mark "Cancelling" then final state only when returns; add `StatusDetail`. |
| Workspace deleted mid-job | Validate at queue time; if missing then immediate Failed. |
| Large output flooding logs | Ring buffer line cap; indicate truncation flag. |
| Terminal too small | Auto-disable status bar/TUI with warning. |
| MCP client disconnect mid-job | Job continues; visible in REPL jobs list. |

## 14. Future Extensions
- Daemon mode (shared per user) using same JobManager; session multiplexing.
- Multi-engine parallel groups (config: `maxParallelScripts`).
- Persistent job history (LiteDB or JSON log file).
- Metrics endpoint (Prometheus exporter or MCP metrics tool).
- Fine-grained progress events (percentComplete).
- Policy engine for disallowing certain script names or commands under MCP.

## 15. Phase 1 Implementation Tasks (Detailed)
1. Domain: Add `Job`, `JobStatus`, `IJobManager`, `JobManager` (queue + worker thread + events).
2. DI: Register as singleton; expose `IJobManager` to REPL + MCP layer.
3. REPL Parser: Add commands `async`, `jobs`, `job <id>` (detail), `view status on|off`, `status`.
4. Execution Integration: Factor existing synchronous script/request invocation into method reused by JobManager.
5. Status Bar: Implement writer; subscribe to job events + workspace activated.
6. MCP Skeleton: Listener + basic JSON-RPC dispatcher; implement workspaces.list, script.run (sync/async), jobs.list, jobs.get.
7. Logging Decorator: Optional minimal ring buffer; if complex, can slip to Phase 2 (replace resultPreview only Phase 1).
8. Localization: Add resource keys for new user-visible messages (jobs.*) in neutral + existing locales (initially fallback if not translated).
9. Tests (where feasible): Job queue ordering, job completion, script failure propagation, jobs.list filtering.
10. Documentation: Update README + add whitepaper reference; command help text.

## 16. Risks & Mitigation Summary
| Risk | Mitigation |
|------|------------|
| ClearScript thread safety breach | Single serialized worker in Phase 1. |
| User confusion over MCP presence | Explicit flag only; clear startup banner. |
| Status bar interfering with output | Separate writer; single-line redraw; toggle off easily. |
| Backwards incompatibility | All features additive & gated by flags. |
| Localization drift | Centralize new keys immediately; fallback to neutral. |

## 17. Open Questions (To Confirm Before Coding)
| Question | Default Assumption |
|----------|--------------------|
| Job retention limit | 200 completed jobs |
| Result preview length | 256 chars |
| Status bar default | Enabled in REPL unless redirected / not TTY |
| Async command alias | Only `async` (no `bg`) initially |
| Support async requests Phase 1 | Yes (mirrors script path) |
| Cancellation Phase 1 | Not yet (graceful reject) |

If any defaults differ from your intent, adjust now before scaffolding.

## 18. Next Step
Upon approval, proceed with Phase 1 scaffolding following task list in Section 15. (Estimated core implementation: ~400–500 LOC excluding tests.)

---
End of document.
