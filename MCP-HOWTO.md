# MCP HOWTO for a2c (Model Context Protocol Integration)

Date: 2025-08-26
Status: Draft (runtime supports TCP JSON-RPC provider mode; auth/policies/schema versioning pending)

---
## 1. Purpose
This document explains how to start, discover, connect to, and drive the a2c MCP server so external agents / LLMs can enumerate and invoke configured HTTP requests and scripts. It also covers the response envelope format, job system, and example client snippets (PowerShell, Node.js, raw JSON). Use this as the integration guide for wiring a2c into automation or agent frameworks (e.g., a future GitHub Copilot MCP extension).

---
## 2. Quick Start TL;DR
1. Start a2c with MCP enabled:
   - `a2c --mcp` (optionally `--mcp-port 5555` or `--mcp-port 0` for auto)
   - or interactive: `a2c mcp start -p 0`
2. Read lock file to discover port: `%USERPROFILE%\.a2c\mcp-server.lock`
3. Open TCP connection to `127.0.0.1:<port>`
4. Read initial `mcp.welcome` JSON line (notification)
5. Send: `{"jsonrpc":"2.0","id":"1","method":"mcp.getCapabilities"}`
6. Use methods: `mcp.runRequest`, `mcp.runScript`, or queue via `mcp.job.submit`.

---
## 3. Starting the MCP Server
### Via root command flags
```
a2c --mcp --mcp-port 0   # start (ephemeral port)
a2c --mcp --mcp-port 5555
```
If already running (externally or in another a2c instance) it reports existing status instead of starting again.

### Via dedicated subcommand
```
a2c mcp start -p 0
```

### Status / Stop
```
a2c mcp status
a2c mcp stop   # only stops if this process owns it (not external)
```

---
## 4. Port & Process Discovery
Lock file: `%USERPROFILE%\\.a2c\\mcp-server.lock`
Example contents:
```json
{"ProcessId":12345,"Port":51423,"StartedUtc":"2025-08-26T18:07:12.3456789Z"}
```
If the file exists and the process is alive, reuse that server. If stale, starting a new server refreshes the file.

---
## 5. Transport & Framing
- Protocol: JSON-RPC 2.0
- Transport: TCP (loopback only for now) on discovered port
- Framing accepted:
  - Newline-delimited JSON (server currently *emits* this)
  - `Content-Length: <n>` + CRLF CRLF + raw JSON payload (server *accepts* either)
- Initial server notification (no `id`):
```json
{"jsonrpc":"2.0","method":"mcp.welcome","params":{"server":"a2c-mcp","pid":12345,"port":51423,"startedUtc":"..."}}
```

---
## 6. Method Surface
All successful responses are wrapped:
```
{"jsonrpc":"2.0","id":"<id>","result":{"status":"ok","type":"<descriptor>","data":{...}[,"elapsedMs":N]}}
```
Errors use standard JSON-RPC error:
```
{"jsonrpc":"2.0","id":"<id>","error":{"code":-32011,"message":"Request not found"}}
```

Method | type (success) | Purpose / Notes
------ | -------------- | ---------------
`mcp.getCapabilities` | `capabilities` | Enumerates methods, feature flags, counts, full request & script metadata summary.
`mcp.ping` | `ping` | Liveness + timestamp + pid + port.
`mcp.getStatus` | `status` | Running/external/port/pid/start time.
`mcp.listWorkspaces` | `workspaceList` | Workspace names & descriptions.
`mcp.listRequests` | `requestList` | All or per-workspace requests (basic fields).
`mcp.listScripts` | `scriptList` | All or per-workspace scripts.
`mcp.describe` | `describe` | Detailed metadata for a single request or script (arguments, headers, etc.).
`mcp.runRequest` | `requestResult` | Executes an HTTP request (can override method/headers/query/body/endpoint).
`mcp.runScript` | `scriptResult` | Executes a script (JavaScript currently) with args.
`mcp.job.submit` | `jobSubmission` | Queues a request or script job (serial queue).
`mcp.job.list` | `jobList` | Lists queued/running/completed jobs.
`mcp.job.get` | `jobStatus` | Status & result/error of a job.
`mcp.job.cancel` | `jobCancel` | Attempts cancellation (if running).

---
## 7. Parameter Schemas (Current Practical Shape)
### runRequest params
```json
{
  "workspace": "<workspaceName>",
  "request": "<requestName>",
  "method": "GET|POST|PUT|PATCH|DELETE" (optional override),
  "endpoint": "/override/path" (optional),
  "headers": { "HeaderName": "Value", ... },
  "query": { "param": "value", ... },
  "body": "raw string payload"
}
```
### runScript params
```json
{
  "script": "<scriptName>",
  "workspace": "<optional workspace>",
  "args": [ "arg1", "arg2" ]
}
```
### job.submit params
```json
{
  "kind": "script" | "request",
  "name": "<scriptOrRequestName>",
  "workspace": "<optional>",
  // For kind=request: same override fields as runRequest
  // For kind=script: args like runScript
}
```

---
## 8. Error Codes (Current Mapping)
Code | Meaning
---- | -------
-32600 | Invalid JSON-RPC request object
-32601 | Method not found
-32602 | Invalid params
-32700 | Parse error
-32000 | HTTP service unavailable / execution infra error
-32001 | Script engine unavailable
-32002 | Script execution failed
-32010 | Workspace not found
-32011 | Request not found
-32012 | Script not found
-32013 | Unsupported HTTP method
-32020 | Job manager unavailable
-32021 | Job submission failure
-32022 | Job not found / cannot cancel
-32030 | Describe target not found

---
## 9. PowerShell Client Helper
```powershell
function Invoke-A2cMcp {
  param([Parameter(Mandatory)][string]$Method,[Hashtable]$Params=@{},[int]$Id=1)
  $lockPath = Join-Path $env:USERPROFILE '.a2c/mcp-server.lock'
  if(-not (Test-Path $lockPath)){ throw "MCP lock file not found." }
  $lock = Get-Content $lockPath | ConvertFrom-Json
  $client = [System.Net.Sockets.TcpClient]::new('127.0.0.1',$lock.Port)
  $stream = $client.GetStream()
  $reader = [System.IO.StreamReader]::new($stream,[Text.Encoding]::UTF8,$true)
  $writer = [System.IO.StreamWriter]::new($stream,(New-Object System.Text.UTF8Encoding($false)))
  $writer.AutoFlush = $true
  $null = $reader.ReadLine() # welcome
  $json = [System.Text.Json.JsonSerializer]::Serialize(@{jsonrpc='2.0';id=$Id;method=$Method;params=$Params})
  $writer.WriteLine($json)
  $line = $reader.ReadLine()
  $client.Dispose()
  return $line | ConvertFrom-Json
}
```
Example:
```powershell
Invoke-A2cMcp -Method mcp.getCapabilities | ConvertTo-Json -Depth 8
Invoke-A2cMcp -Method mcp.runScript -Params @{ script='hello'; args=@('world') }
```

---
## 10. Node.js Minimal Client
```js
const fs = require('fs');
const net = require('net');
const lock = JSON.parse(fs.readFileSync(process.env.USERPROFILE+'/.a2c/mcp-server.lock','utf8'));
const sock = net.createConnection({host:'127.0.0.1', port:lock.Port});
let first = true;
function send(obj){ sock.write(JSON.stringify(obj)+'\n'); }

sock.on('data', d => {
  d.toString().trim().split(/\n+/).forEach(line => {
    if(!line) return;
    console.log('RX', line);
  });
});

sock.on('connect', () => {
  // welcome comes first automatically
  send({jsonrpc:'2.0', id:'1', method:'mcp.getCapabilities'});
  send({jsonrpc:'2.0', id:'2', method:'mcp.ping'});
});
```

---
## 11. Sample Request / Response (runRequest)
Request:
```json
{"jsonrpc":"2.0","id":"10","method":"mcp.runRequest","params":{"workspace":"demo","request":"GetUsers"}}
```
Success response (truncated):
```json
{
  "jsonrpc":"2.0","id":"10","result":{
    "status":"ok","type":"requestResult","elapsedMs":42,
    "data":{"status":"OK","code":200,"body":"...","headers":{...},"url":"https://.../users","method":"GET"}
  }
}
```

---
## 12. Jobs (Long Running)
Submit:
```json
{"jsonrpc":"2.0","id":"20","method":"mcp.job.submit","params":{"kind":"script","name":"longTask"}}
```
Response:
```json
{"jsonrpc":"2.0","id":"20","result":{"status":"ok","type":"jobSubmission","data":{"jobId":"<guid>","status":"Queued","queuedAt":"2025-08-26T...Z"}}}
```
Poll:
```json
{"jsonrpc":"2.0","id":"21","method":"mcp.job.get","params":{"jobId":"<guid>"}}
```
Cancel:
```json
{"jsonrpc":"2.0","id":"22","method":"mcp.job.cancel","params":{"jobId":"<guid>"}}
```

---
## 13. Describe vs List
- `mcp.listRequests` returns lightweight definitions.
- `mcp.describe` (kind=request/script) returns full argument metadata, headers, cookies, language, body preview (scripts) for more precise tool modeling.

---
## 14. Integration Guidance for Agents / LLMs
1. Open connection → read welcome.
2. Call `mcp.getCapabilities` to build an internal tool registry (requests + scripts).
3. For each candidate tool invocation, optionally refine with `mcp.describe` to surface argument defaults & headers.
4. For immediate HTTP or script operations, choose direct `mcp.runRequest` / `mcp.runScript`.
5. For potentially long-running operations (or if you want cancellability), submit via `mcp.job.submit` and poll.
6. Handle errors by inspecting `error.code`; map to retry / user feedback policies.
7. Cache capabilities; refresh periodically (e.g., every few minutes) or on error -32011/-32012 (resource not found) to detect dynamic changes.

---
## 15. Extending to GitHub Copilot MCP (Future Plan)
GitHub Copilot's MCP integration (when allowing custom TCP providers) would require:
- A host adapter exposing this TCP endpoint via stdio or WebSocket (if Copilot limits transports). A thin proxy can translate stdio <-> TCP.
- Optional authentication (not yet implemented) to prevent unintended local access.
- JSON schema emission (future) for tool arguments to enable automatic UI / validation.
For now, treat this runtime as a local provider; build a simple proxy if a client demands stdio.

---
## 16. Limitations / Roadmap Hooks
Area | Current | Planned Next
-----|---------|-------------
Auth | None | API key / allowlist
Policies | None | Timeouts, consent flags
Schema | Implicit only | JSON Schema generation
Versioning | Assembly version only | Semantic tool versions + hash
Error Envelopes | JSON-RPC error only | Optional uniform error meta
Streaming | Not available | Job progress / events
Toolbelt | Workspaces & scripts only | Safe FS / SQL / HTTP fetch modules

---
## 17. Troubleshooting
Symptom | Cause | Fix
------- | ----- | ---
Cannot connect (ECONNREFUSED) | Server not started | Start with `a2c --mcp`
Lock file missing | Server not started / cleaned | Start server again
Lock file present but dead port | Stale process | Start server; stale file auto-replaced
Error -32011 | Request name mismatch | Re-run capabilities; check workspace
Error -32012 | Script not found | Same as above
Error -32013 | Unsupported HTTP method override | Use GET/POST/PUT/PATCH/DELETE
Script exec failed (-32002) | Runtime exception in script | Inspect `message`; adjust script

---
## 18. Security Notes (Current State)
- Loopback only: reachable only from local machine.
- No authentication yet: treat as dev-only; do not expose via port forwarding.

---
## 19. Glossary
Term | Meaning
---- | -------
Workspace | Logical grouping of configured HTTP requests & scripts
Request | Declarative HTTP invocation template (method, endpoint, arguments)
Script | Executable script (JS) with optional arguments
Job | Queued execution wrapper for longer or cancellable operations
Describe | Detailed inspection of a single request or script

---
## 20. Changelog (Doc)
2025-08-26: Initial draft.

---
## 21. Contact / Contributions
Open issues or propose enhancements in the repository. Priority items: schema generation, auth, policy engine, stdio/WebSocket proxy.

---
## 22. GitHub Copilot Bridge (StdIO <-> TCP)
To let GitHub Copilot (or any stdio-only MCP host) talk to the running TCP server, use the included bridge project.

Build the bridge:
```
dotnet build -c Release a2c.mcp.bridge
```

Run it (connect to existing a2c MCP on fixed port):
```
dotnet run --project a2c.mcp.bridge -- --port 61658
```
Or with env vars:
```
set A2C_MCP_PORT=61658
dotnet run --project a2c.mcp.bridge
```

In VS Code `settings.json` (example draft schema):
```json
{
  "github.copilot.chat.modelContextProviders": [
    {
      "id": "a2c-mcp",
      "title": "a2c MCP",
      "command": "dotnet",
      "args": ["run","--project","c:/absolute/path/to/Api2Cli/a2c.mcp.bridge"],
      "env": { "A2C_MCP_PORT": "61658" },
      "description": "StdIO bridge to local a2c MCP server"
    }
  ]
}
```
Then restart VS Code / Copilot. The bridge prints connection status to stderr, while forwarding JSON-RPC lines between Copilot and a2c.

Bridge options:
- `--port <n>` (or env `A2C_MCP_PORT`)
- `--host <addr>` (default 127.0.0.1 / env `A2C_MCP_HOST`)
- `--retries <n>` (default 30)
- `--retry-delay <ms>` (default 1000)

If a2c restarts, restart the bridge (future enhancement: auto re-dial loop retained only for initial connect).

Security: Bridge inherits current loopback-only security posture (no auth). Do not expose externally without adding authentication.

---
END.
