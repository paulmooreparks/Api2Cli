using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.Orchestration.Services;
using ParksComputing.Api2Cli.Http.Services;
using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Runtime.Services.Jobs;

namespace ParksComputing.Api2Cli.Runtime.Services.Mcp;

public record McpServerStatus(bool IsRunning, bool IsExternal, int? Port, int? ProcessId, DateTime? StartedUtc, string? Message = null);

public interface IMcpServerManager {
    McpServerStatus Status { get; }
    Task<McpServerStatus> StartAsync(int port = 0, CancellationToken cancellationToken = default);
    Task<McpServerStatus> StopAsync(CancellationToken cancellationToken = default);
    McpServerStatus Refresh();
    int? PreferredPort { get; set; }
}

internal sealed class McpServerManager : IMcpServerManager, IDisposable {
    private readonly object _gate = new();
    private readonly string _lockFilePath;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _serverLoop;
    private DateTime? _startedUtc;
    private int? _port;
    private int? _preferredPort;
    private bool _disposed;

    public McpServerStatus Status => BuildStatus();

    private readonly IWorkspaceService _workspaceService;

    private readonly IApi2CliScriptEngineFactory? _scriptEngineFactory; // optional for script method
    private readonly IWorkspaceScriptingOrchestrator? _orchestrator;
    private readonly IHttpService? _httpService;
    private readonly A2CApi? _a2cApi;
    private readonly IJobManager? _jobManager;

    // Custom error codes (JSON-RPC reserved range: -32000 to -32099 for implementation defined)
    private static class Err {
        public const int HttpUnavailable = -32000;
        public const int ScriptUnavailable = -32001;
        public const int ScriptExecFailed = -32002;
        public const int WorkspaceNotFound = -32010;
        public const int RequestNotFound = -32011;
        public const int ScriptNotFound = -32012;
        public const int UnsupportedMethod = -32013;
        public const int InvalidParams = -32602; // standard
    }

    public McpServerManager(Workspace.Models.WorkspaceRuntimeOptions runtimeOptions,
                            IWorkspaceService workspaceService,
                            IApi2CliScriptEngineFactory? scriptEngineFactory,
                            IWorkspaceScriptingOrchestrator? orchestrator,
                            IHttpService? httpService,
                            A2CApi? a2cApi,
                            IJobManager? jobManager) {
        _workspaceService = workspaceService;
        _scriptEngineFactory = scriptEngineFactory;
        _orchestrator = orchestrator;
        _httpService = httpService;
        _a2cApi = a2cApi;
        _jobManager = jobManager;
        var root = runtimeOptions.ConfigRoot;
        if (string.IsNullOrWhiteSpace(root)) {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Workspace.Constants.Api2CliDirectoryName);
        }
        Directory.CreateDirectory(root!);
        _lockFilePath = Path.Combine(root!, "mcp-server.lock");
    }

    public McpServerStatus Refresh() {
        lock (_gate) {
            return BuildStatus(checkExternal: true);
        }
    }

    public int? PreferredPort { get { lock (_gate) return _preferredPort; } set { lock (_gate) _preferredPort = value; } }

    public Task<McpServerStatus> StartAsync(int port = 0, CancellationToken cancellationToken = default) {
        lock (_gate) {
            // If already running internally just report
            if (_listener is not null) {
                return Task.FromResult(BuildStatus());
            }
            // Check for external instance
            var external = ReadLockFile();
            if (external.HasValue && IsProcessAlive(external.Value.ProcessId)) {
                var ex = external.Value;
                return Task.FromResult(new McpServerStatus(true, true, ex.Port, ex.ProcessId, ex.StartedUtc, "External MCP server already running."));
            }
            // Stale lock file -> delete
            if (external.HasValue) {
                TryDeleteLock();
            }
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var effectivePort = port;
            if (effectivePort == 0 && _preferredPort.HasValue && _preferredPort.Value > 0) {
                effectivePort = _preferredPort.Value;
            }
            _listener = new TcpListener(IPAddress.Loopback, effectivePort);
            _listener.Start();
            _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _startedUtc = DateTime.UtcNow;
            WriteLockFile();
            _serverLoop = Task.Run(() => RunLoopAsync(_cts.Token));
            return Task.FromResult(BuildStatus());
        }
    }

    public async Task<McpServerStatus> StopAsync(CancellationToken cancellationToken = default) {
        TcpListener? listener;
        CancellationTokenSource? cts;
        Task? loop;
        lock (_gate) {
            listener = _listener;
            cts = _cts;
            loop = _serverLoop;
            _listener = null;
            _cts = null;
            _serverLoop = null;
            _startedUtc = null;
            _port = null;
            TryDeleteLock();
        }
        try { listener?.Stop(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[McpServer] listener stop failed: {ex.Message}"); }
        try { if (cts is not null && !cts.IsCancellationRequested) cts.Cancel(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[McpServer] cancel token failed: {ex.Message}"); }
        if (loop is not null) {
            try { await loop.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[McpServer] server loop wait failed: {ex.Message}"); }
        }
        return BuildStatus();
    }

    private async Task RunLoopAsync(CancellationToken token) {
        while (!token.IsCancellationRequested) {
            try {
                var client = await _listener!.AcceptTcpClientAsync(token).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, token), token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception) {
                await Task.Delay(250, token).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token) {
        using var c = client;
        NetworkStream? stream = null;
        StreamReader? reader = null;
        StreamWriter? writer = null;
        try {
            stream = c.GetStream();
            reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks:true, leaveOpen:true);
            writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            // Send welcome notification (JSON-RPC style notification)
            var welcome = new {
                jsonrpc = "2.0",
                method = "mcp.welcome",
                paramsObj = new { server = "a2c-mcp", pid = Environment.ProcessId, port = _port, startedUtc = _startedUtc }
            };
            var welcomeJson = JsonSerializer.Serialize(new JsonObject{
                ["jsonrpc"] = "2.0",
                ["method"] = "mcp.welcome",
                ["params"] = JsonSerializer.SerializeToNode(welcome.paramsObj)
            });
            await writer.WriteLineAsync(welcomeJson).ConfigureAwait(false);

            while (!token.IsCancellationRequested) {
                string? line;
                try {
                    line = await ReadNextMessageAsync(reader, token).ConfigureAwait(false);
                } catch (OperationCanceledException) { break; }
                if (line == null) break; // client closed
                if (string.IsNullOrWhiteSpace(line)) continue; // skip heartbeat
                JsonNode? root = null;
                string? id = null;
                try {
                    root = JsonNode.Parse(line);
                    id = root?["id"]?.ToString();
                } catch (Exception pex) {
                    await SendErrorAsync(writer, id, -32700, "Parse error").ConfigureAwait(false);
                    System.Diagnostics.Debug.WriteLine($"[McpServer] parse error: {pex.Message}");
                    continue;
                }
                var method = root?["method"]?.ToString();
                if (string.IsNullOrEmpty(method)) {
                    await SendErrorAsync(writer, id, -32600, "Invalid Request").ConfigureAwait(false);
                    continue;
                }
                var @params = root?["params"];
                switch (method) {
                    case "mcp.getCapabilities":
                        {
                            var caps = BuildCapabilities();
                            var envelope = new JsonObject {
                                ["status"] = "ok",
                                ["type"] = "capabilities",
                                ["data"] = caps
                            };
                            await SendResultAsync(writer, id, envelope).ConfigureAwait(false);
                        }
                        break;
                    case "mcp.ping":
                        {
                            var payload = new JsonObject {
                                ["timeUtc"] = DateTime.UtcNow.ToString("o"),
                                ["pid"] = Environment.ProcessId,
                                ["port"] = _port
                            };
                            var envelope = new JsonObject { ["status"] = "ok", ["type"] = "ping", ["data"] = payload };
                            await SendResultAsync(writer, id, envelope).ConfigureAwait(false);
                        }
                        break;
                    case "mcp.getStatus":
                        var st = BuildStatus();
                        var statusPayload = new JsonObject {
                            ["running"] = st.IsRunning,
                            ["external"] = st.IsExternal,
                            ["port"] = st.Port,
                            ["pid"] = st.ProcessId,
                            ["startedUtc"] = st.StartedUtc?.ToString("o")
                        };
                        var statusEnvelope = new JsonObject { ["status"] = "ok", ["type"] = "status", ["data"] = statusPayload };
                        await SendResultAsync(writer, id, statusEnvelope).ConfigureAwait(false);
                        break;
                    case "mcp.listWorkspaces":
                        var ws = _workspaceService.BaseConfig?.Workspaces ?? new();
                        var arr = new JsonArray();
                        foreach (var kvp in ws) {
                            arr.Add(new JsonObject {
                                ["name"] = kvp.Key,
                                ["description"] = kvp.Value.Description ?? string.Empty
                            });
                        }
                        var wsPayload = new JsonObject { ["workspaces"] = arr };
                        var wsEnvelope = new JsonObject { ["status"] = "ok", ["type"] = "workspaceList", ["data"] = wsPayload };
                        await SendResultAsync(writer, id, wsEnvelope).ConfigureAwait(false);
                        break;
                    case "mcp.listRequests":
                        await HandleListRequestsAsync(writer, id, @params).ConfigureAwait(false);
                        break;
                    case "mcp.listScripts":
                        await HandleListScriptsAsync(writer, id, @params).ConfigureAwait(false);
                        break;
                    case "mcp.runRequest":
                        await HandleRunRequestAsync(writer, id, @params).ConfigureAwait(false);
                        break;
                    case "mcp.runScript":
                        await HandleRunScriptAsync(writer, id, @params).ConfigureAwait(false);
                        break;
                    case "mcp.job.submit":
                        await HandleJobSubmitAsync(writer, id, @params).ConfigureAwait(false);
                        break;
                    case "mcp.job.list":
                        await HandleJobListAsync(writer, id, @params).ConfigureAwait(false);
                        break;
                    case "mcp.job.get":
                        await HandleJobGetAsync(writer, id, @params).ConfigureAwait(false);
                        break;
                    case "mcp.job.cancel":
                        await HandleJobCancelAsync(writer, id, @params).ConfigureAwait(false);
                        break;
                    case "mcp.describe":
                        await HandleDescribeAsync(writer, id, @params).ConfigureAwait(false);
                        break;
                    default:
                        await SendErrorAsync(writer, id, -32601, "Method not found").ConfigureAwait(false);
                        break;
                }
            }
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"[McpServer] connection handler error: {ex.Message}");
        }
    }

    private JsonObject BuildCapabilities() {
        // Dynamic, discoverable capability surface including requests & scripts (with arguments/options)
        var methods = new JsonArray(
            "mcp.getCapabilities",
            "mcp.ping",
            "mcp.getStatus",
            "mcp.listWorkspaces",
            "mcp.runRequest",
            "mcp.runScript",
            "mcp.job.submit",
            "mcp.job.list",
            "mcp.job.get",
            "mcp.job.cancel",
            "mcp.listRequests",
            "mcp.listScripts",
            "mcp.describe"
        );

        var requestsArr = new JsonArray();
        int requestCount = 0;
        foreach (var (wsName, wsDef) in _workspaceService.BaseConfig.Workspaces) {
            foreach (var (reqName, reqDef) in wsDef.Requests) {
                var argsArr = new JsonArray();
                if (reqDef.Arguments is not null) {
                    foreach (var a in reqDef.Arguments.Values) {
                        argsArr.Add(new JsonObject {
                            ["name"] = a.Name ?? string.Empty,
                            ["type"] = a.Type ?? string.Empty,
                            ["description"] = a.Description ?? string.Empty,
                            ["required"] = a.IsRequired,
                            ["default"] = a.Default is null ? null : JsonValue.Create(a.Default)
                        });
                    }
                }
                var paramArr = new JsonArray();
                if (reqDef.Parameters is not null) {
                    foreach (var p in reqDef.Parameters) paramArr.Add(p);
                }
                requestsArr.Add(new JsonObject {
                    ["workspace"] = wsName,
                    ["name"] = reqName,
                    ["method"] = reqDef.Method ?? "GET",
                    ["endpoint"] = reqDef.Endpoint ?? string.Empty,
                    ["description"] = reqDef.Description ?? string.Empty,
                    ["arguments"] = argsArr,
                    ["parameters"] = paramArr,
                    ["hasPayload"] = !string.IsNullOrWhiteSpace(reqDef.Payload)
                });
                requestCount++;
            }
        }

        var scriptsArr = new JsonArray();
        int scriptCount = 0;
        // Global scripts (no workspace)
        foreach (var (scriptName, scriptDef) in _workspaceService.BaseConfig.Scripts) {
            scriptsArr.Add(BuildScriptCapability(null, scriptName, scriptDef));
            scriptCount++;
        }
        // Workspace scripts
        foreach (var (wsName, wsDef) in _workspaceService.BaseConfig.Workspaces) {
            foreach (var (scriptName, scriptDef) in wsDef.Scripts) {
                scriptsArr.Add(BuildScriptCapability(wsName, scriptName, scriptDef));
                scriptCount++;
            }
        }

        var wsCount = _workspaceService.BaseConfig.Workspaces.Count;
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        return new JsonObject {
            ["version"] = version,
            ["timestampUtc"] = DateTime.UtcNow.ToString("o"),
            ["methods"] = methods,
            ["features"] = new JsonArray("httpExecution","scriptExecution","jobs"),
            ["framing"] = new JsonObject { ["contentLength"] = true, ["lineDelimited"] = true },
            ["counts"] = new JsonObject {
                ["workspaces"] = wsCount,
                ["requests"] = requestCount,
                ["scripts"] = scriptCount
            },
            ["requests"] = requestsArr,
            ["scripts"] = scriptsArr
        };

        JsonObject BuildScriptCapability(string? wsName, string scriptName, ParksComputing.Api2Cli.Workspace.Models.ScriptDefinition scriptDef) {
            var argsArr = new JsonArray();
            if (scriptDef.Arguments is not null) {
                foreach (var a in scriptDef.Arguments.Values) {
                    argsArr.Add(new JsonObject {
                        ["name"] = a.Name ?? string.Empty,
                        ["type"] = a.Type ?? string.Empty,
                        ["description"] = a.Description ?? string.Empty,
                        ["required"] = a.IsRequired,
                        ["default"] = a.Default is null ? null : JsonValue.Create(a.Default)
                    });
                }
            }
            var (lang, _) = scriptDef.ResolveLanguageAndBody();
            return new JsonObject {
                ["workspace"] = wsName is null ? JsonValue.Create<string?>(null) : wsName,
                ["name"] = scriptName,
                ["description"] = scriptDef.Description ?? string.Empty,
                ["language"] = lang,
                ["arguments"] = argsArr
            };
        }
    }

    // Support either line-delimited JSON or Content-Length framing (basic implementation)
    private static async Task<string?> ReadNextMessageAsync(StreamReader reader, CancellationToken token) {
        reader.Peek(); // force buffer fill
        // Look ahead: if next line starts with Content-Length we parse framed message
        var headerLine = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromMinutes(5), token).ConfigureAwait(false);
        if (headerLine is null) return null;
        if (headerLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) {
            if (!int.TryParse(headerLine.AsSpan("Content-Length:".Length).Trim(), out var length) || length < 0 || length > 10_000_000) {
                // invalid length -> skip payload
                return null;
            }
            // Expect blank separator line
            var blank = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            if (blank is null) return null;
            var buffer = new char[length];
            var read = 0;
            while (read < length) {
                var n = await reader.ReadAsync(buffer.AsMemory(read, length - read), token).ConfigureAwait(false);
                if (n == 0) break;
                read += n;
            }
            if (read != length) return null;
            return new string(buffer);
        }
        // Otherwise treat first line as payload (if not empty). If empty continue reading.
        if (string.IsNullOrWhiteSpace(headerLine)) return headerLine; // keep skip semantics outside
        return headerLine;
    }

    private async Task HandleRunRequestAsync(StreamWriter writer, string? id, JsonNode? @params) {
        if (id is null) return; // ignore notifications
        try {
            var exec = await ExecuteRequestInternalAsync(@params).ConfigureAwait(false);
            if (exec.Error is not null) {
                await SendErrorAsync(writer, id, exec.Error.Code, exec.Error.Message!).ConfigureAwait(false);
                return;
            }
            // Standard envelope
            var envelope = new JsonObject {
                ["status"] = "ok",
                ["type"] = "requestResult",
                ["elapsedMs"] = exec.Payload? ["elapsedMs"],
                ["data"] = exec.Payload
            };
            await SendResultAsync(writer, id, envelope).ConfigureAwait(false);
        } catch (Exception ex) {
            await SendErrorAsync(writer, id!, Err.HttpUnavailable, ex.Message).ConfigureAwait(false);
        }
    }

    private async Task HandleRunScriptAsync(StreamWriter writer, string? id, JsonNode? @params) {
        if (id is null) return;
        try {
            var exec = await ExecuteScriptInternalAsync(@params).ConfigureAwait(false);
            if (exec.Error is not null) {
                await SendErrorAsync(writer, id, exec.Error.Code, exec.Error.Message!).ConfigureAwait(false);
                return;
            }
            var envelope = new JsonObject {
                ["status"] = "ok",
                ["type"] = "scriptResult",
                ["elapsedMs"] = exec.Payload? ["elapsedMs"],
                ["data"] = exec.Payload
            };
            await SendResultAsync(writer, id, envelope).ConfigureAwait(false);
        } catch (Exception ex) {
            await SendErrorAsync(writer, id!, Err.ScriptUnavailable, ex.Message).ConfigureAwait(false);
        }
    }

    private sealed record ExecError(int Code, string? Message);
    private sealed record ExecResult(JsonObject? Payload, ExecError? Error);

    private async Task<ExecResult> ExecuteRequestInternalAsync(JsonNode? @params) {
        try {
            var workspace = @params?["workspace"]?.ToString();
            var request = @params?["request"]?.ToString();
            if (string.IsNullOrWhiteSpace(workspace) || string.IsNullOrWhiteSpace(request)) return new(null, new(Err.InvalidParams, "Missing workspace or request"));
            if (!_workspaceService.BaseConfig.Workspaces.TryGetValue(workspace, out var wsDef)) return new(null, new(Err.WorkspaceNotFound, "Workspace not found"));
            if (!wsDef.Requests.TryGetValue(request, out var reqDef)) return new(null, new(Err.RequestNotFound, "Request not found"));
            var baseUrl = wsDef.BaseUrl;
            if (string.IsNullOrEmpty(baseUrl)) return new(null, new(Err.InvalidParams, "Workspace baseUrl missing"));
            var overrideMethod = @params?["method"]?.ToString();
            var overrideEndpoint = @params?["endpoint"]?.ToString();
            var dynamicHeaders = @params?["headers"] as JsonObject;
            var dynamicQuery = @params?["query"] as JsonObject;
            var overrideBody = @params?["body"]?.ToString();
            var method = (overrideMethod ?? reqDef.Method ?? "GET").ToUpperInvariant();
            if (_httpService is null) return new(null, new(Err.HttpUnavailable, "HTTP service unavailable"));
            var payload = overrideBody ?? reqDef.Payload;
            var headers = reqDef.Headers?.ToDictionary(h => h.Key, h => h.Value) ?? new Dictionary<string, string>();
            if (dynamicHeaders is not null) foreach (var kv in dynamicHeaders) if (kv.Value is not null) headers[kv.Key] = kv.Value!.ToString();
            var parameters = new Dictionary<string, string?>();
            foreach (var p in reqDef.Parameters) { var sp = p.Split('=',2); parameters[sp[0]] = sp.Length>1? sp[1]: null; }
            if (dynamicQuery is not null) foreach (var kv in dynamicQuery) if (kv.Value is not null) parameters[kv.Key] = kv.Value!.ToString();
            var headerSeq = headers.Select(h => h.Key + ":" + h.Value);
            var paramSeq = parameters.Select(p => p.Value is not null ? p.Key + "=" + p.Value : p.Key);
            var url = baseUrl + (overrideEndpoint ?? reqDef.Endpoint);
            HttpResponseMessage? resp = null;
            var sw = Stopwatch.StartNew();
            try {
                resp = method switch {
                    "GET" => await _httpService.GetAsync(url, paramSeq, headerSeq).ConfigureAwait(false),
                    "POST" => await _httpService.PostAsync(url, payload, headerSeq).ConfigureAwait(false),
                    "PUT" => await _httpService.PutAsync(url, payload, headerSeq).ConfigureAwait(false),
                    "PATCH" => await _httpService.PatchAsync(url, payload, headerSeq).ConfigureAwait(false),
                    "DELETE" => await _httpService.DeleteAsync(url, headerSeq).ConfigureAwait(false),
                    _ => null
                };
                if (resp is null) return new(null, new(Err.UnsupportedMethod, "Unsupported method"));
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                sw.Stop();
                var headerObj = new JsonObject();
                foreach (var h in resp.Headers) headerObj[h.Key] = string.Join(",", h.Value);
                foreach (var h in resp.Content.Headers) headerObj[h.Key] = string.Join(",", h.Value);
                return new(new JsonObject {
                    ["status"] = resp.StatusCode.ToString(),
                    ["code"] = (int)resp.StatusCode,
                    ["body"] = body,
                    ["elapsedMs"] = sw.ElapsedMilliseconds,
                    ["headers"] = headerObj,
                    ["url"] = url,
                    ["method"] = method
                }, null);
            } catch (Exception ex) {
                return new(null, new(Err.HttpUnavailable, ex.Message));
            }
        } catch (Exception ex) {
            return new(null, new(Err.HttpUnavailable, ex.Message));
        }
    }

    private Task<ExecResult> ExecuteScriptInternalAsync(JsonNode? @params) {
        try {
            var workspace = @params?["workspace"]?.ToString();
            var script = @params?["script"]?.ToString();
            if (string.IsNullOrWhiteSpace(script)) return Task.FromResult(new ExecResult(null, new(Err.InvalidParams, "Missing script")));
            if (!string.IsNullOrEmpty(workspace)) {
                if (!_workspaceService.BaseConfig.Workspaces.ContainsKey(workspace)) return Task.FromResult(new ExecResult(null, new(Err.WorkspaceNotFound, "Workspace not found")));
                _workspaceService.SetActiveWorkspace(workspace);
                _orchestrator?.ActivateWorkspace(workspace);
            }
            if (_scriptEngineFactory is null) return Task.FromResult(new ExecResult(null, new(Err.ScriptUnavailable, "Script engine unavailable")));
            ParksComputing.Api2Cli.Workspace.Models.ScriptDefinition? scriptDef = null;
            if (string.IsNullOrEmpty(workspace)) _workspaceService.BaseConfig.Scripts.TryGetValue(script, out scriptDef); else _workspaceService.BaseConfig.Workspaces[workspace].Scripts.TryGetValue(script, out scriptDef);
            if (scriptDef is null) return Task.FromResult(new ExecResult(null, new(Err.ScriptNotFound, "Script not found")));
            var argsArray = new List<string>();
            if (@params?["args"] is JsonArray ja) foreach (var v in ja) if (v is not null) argsArray.Add(v.ToString());
            var resolved = scriptDef.ResolveLanguageAndBody();
            var lang = resolved.Lang ?? "javascript";
            var engine = _scriptEngineFactory.GetEngine(lang);
            object? result = null;
            var sw = Stopwatch.StartNew();
            try {
                var (_, body) = resolved;
                if (argsArray.Count > 0 && lang.Equals("javascript", StringComparison.OrdinalIgnoreCase)) engine.SetValue("a2cArgs", argsArray.ToArray());
                result = engine.EvaluateScript(body);
            } catch (Exception sex) {
                return Task.FromResult(new ExecResult(null, new(Err.ScriptExecFailed, "Script execution failed: " + sex.Message)));
            }
            sw.Stop();
            return Task.FromResult(new ExecResult(new JsonObject {
                ["result"] = result?.ToString() ?? string.Empty,
                ["elapsedMs"] = sw.ElapsedMilliseconds,
                ["language"] = lang,
                ["args"] = new JsonArray(argsArray.Select(a => (JsonNode?)a).ToArray())
            }, null));
        } catch (Exception ex) {
            return Task.FromResult(new ExecResult(null, new(Err.ScriptUnavailable, ex.Message)));
        }
    }

    private Task HandleListRequestsAsync(StreamWriter writer, string? id, JsonNode? @params) {
        if (id is null) return Task.CompletedTask;
        var workspace = @params? ["workspace"]?.ToString();
        var arr = new JsonArray();
        if (string.IsNullOrWhiteSpace(workspace)) {
            // list all requests across all workspaces (flatten with workspace name)
            foreach (var ws in _workspaceService.BaseConfig.Workspaces) {
                foreach (var req in ws.Value.Requests) {
                    arr.Add(new JsonObject {
                        ["workspace"] = ws.Key,
                        ["name"] = req.Key,
                        ["method"] = req.Value.Method ?? "GET",
                        ["endpoint"] = req.Value.Endpoint ?? string.Empty,
                        ["description"] = req.Value.Description ?? string.Empty
                    });
                }
            }
        } else {
            if (_workspaceService.BaseConfig.Workspaces.TryGetValue(workspace, out var wsDef)) {
                foreach (var req in wsDef.Requests) {
                    arr.Add(new JsonObject {
                        ["workspace"] = workspace,
                        ["name"] = req.Key,
                        ["method"] = req.Value.Method ?? "GET",
                        ["endpoint"] = req.Value.Endpoint ?? string.Empty,
                        ["description"] = req.Value.Description ?? string.Empty
                    });
                }
            }
        }
    var payload = new JsonObject { ["requests"] = arr };
    var envelope = new JsonObject { ["status"] = "ok", ["type"] = "requestList", ["data"] = payload };
    return SendResultAsync(writer, id, envelope);
    }

    private Task HandleListScriptsAsync(StreamWriter writer, string? id, JsonNode? @params) {
        if (id is null) return Task.CompletedTask;
        var workspace = @params? ["workspace"]?.ToString();
        var arr = new JsonArray();
        if (string.IsNullOrWhiteSpace(workspace)) {
            foreach (var s in _workspaceService.BaseConfig.Scripts) {
                arr.Add(new JsonObject {
                    ["workspace"] = JsonValue.Create<string?>(null),
                    ["name"] = s.Key,
                    ["description"] = s.Value.Description ?? string.Empty,
                    ["language"] = s.Value.ScriptLanguage ?? s.Value.ScriptTags?.FirstOrDefault() ?? "javascript"
                });
            }
            foreach (var ws in _workspaceService.BaseConfig.Workspaces) {
                foreach (var s in ws.Value.Scripts) {
                    arr.Add(new JsonObject {
                        ["workspace"] = ws.Key,
                        ["name"] = s.Key,
                        ["description"] = s.Value.Description ?? string.Empty,
                        ["language"] = s.Value.ScriptLanguage ?? s.Value.ScriptTags?.FirstOrDefault() ?? "javascript"
                    });
                }
            }
        } else {
            if (_workspaceService.BaseConfig.Workspaces.TryGetValue(workspace, out var wsDef)) {
                foreach (var s in wsDef.Scripts) {
                    arr.Add(new JsonObject {
                        ["workspace"] = workspace,
                        ["name"] = s.Key,
                        ["description"] = s.Value.Description ?? string.Empty,
                        ["language"] = s.Value.ScriptLanguage ?? s.Value.ScriptTags?.FirstOrDefault() ?? "javascript"
                    });
                }
            }
        }
    var payload = new JsonObject { ["scripts"] = arr };
    var envelope = new JsonObject { ["status"] = "ok", ["type"] = "scriptList", ["data"] = payload };
    return SendResultAsync(writer, id, envelope);
    }

    private async Task HandleJobSubmitAsync(StreamWriter writer, string? id, JsonNode? @params) {
        if (id is null) return;
        if (_jobManager is null) { await SendErrorAsync(writer, id, -32020, "Job manager unavailable").ConfigureAwait(false); return; }
        try {
            var kind = @params? ["kind"]?.ToString() ?? string.Empty; // script|request
            var name = @params? ["name"]?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(name)) { await SendErrorAsync(writer, id, Err.InvalidParams, "Missing kind or name").ConfigureAwait(false); return; }
            // For phase 1, reuse existing handlers synchronously inside job work
            var argsList = new List<object?>();
            if (@params? ["args"] is JsonArray ja) foreach (var v in ja) argsList.Add(v?.GetValue<string>());
            JobRequest req = kind switch {
                "script" => new JobRequest("script", name, async ct => {
                    var scriptParams = new JsonObject { ["script"] = name, ["workspace"] = @params?["workspace"], ["args"] = @params?["args"] };
                    var exec = await ExecuteScriptInternalAsync(scriptParams).ConfigureAwait(false);
                    return exec.Error is null ? exec.Payload : new JsonObject { ["error"] = exec.Error.Message };
                }),
                "request" => new JobRequest("request", name, async ct => {
                    var requestParams = new JsonObject { ["request"] = name, ["workspace"] = @params?["workspace"], ["method"] = @params?["method"], ["endpoint"] = @params?["endpoint"], ["headers"] = @params?["headers"], ["query"] = @params?["query"], ["body"] = @params?["body"] };
                    var exec = await ExecuteRequestInternalAsync(requestParams).ConfigureAwait(false);
                    return exec.Error is null ? exec.Payload : new JsonObject { ["error"] = exec.Error.Message };
                }),
                _ => new JobRequest(kind, name, _ => Task.FromResult<object?>(null))
            };
            var job = _jobManager.Enqueue(req);
            var payload = new JsonObject {
                ["jobId"] = job.Id.ToString(),
                ["status"] = job.Status.ToString(),
                ["queuedAt"] = job.QueuedAt.ToString("o")
            };
            var envelope = new JsonObject {
                ["status"] = "ok",
                ["type"] = "jobSubmission",
                ["data"] = payload
            };
            await SendResultAsync(writer, id, envelope).ConfigureAwait(false);
        } catch (Exception ex) {
            await SendErrorAsync(writer, id, -32021, ex.Message).ConfigureAwait(false);
        }
    }

    private Task HandleJobListAsync(StreamWriter writer, string? id, JsonNode? @params) {
        if (id is null) return Task.CompletedTask;
        if (_jobManager is null) return SendErrorAsync(writer, id, -32020, "Job manager unavailable");
        var arr = new JsonArray();
        foreach (var j in _jobManager.Jobs) {
            arr.Add(new JsonObject {
                ["id"] = j.Id.ToString(),
                ["kind"] = j.Kind,
                ["name"] = j.Name,
                ["status"] = j.Status.ToString(),
                ["queuedAt"] = j.QueuedAt.ToString("o"),
                ["startedAt"] = j.StartedAt?.ToString("o"),
                ["completedAt"] = j.CompletedAt?.ToString("o")
            });
        }
        var payload = new JsonObject { ["jobs"] = arr };
        var envelope = new JsonObject {
            ["status"] = "ok",
            ["type"] = "jobList",
            ["data"] = payload
        };
        return SendResultAsync(writer, id, envelope);
    }

    private Task HandleJobGetAsync(StreamWriter writer, string? id, JsonNode? @params) {
        if (id is null) return Task.CompletedTask;
        if (_jobManager is null) return SendErrorAsync(writer, id, -32020, "Job manager unavailable");
        var jid = @params? ["jobId"]?.ToString();
        if (string.IsNullOrWhiteSpace(jid) || !Guid.TryParse(jid, out var guid)) return SendErrorAsync(writer, id, Err.InvalidParams, "Missing or invalid jobId");
        if (!_jobManager.TryGet(guid, out var job)) return SendErrorAsync(writer, id, -32022, "Job not found");
        var payload = new JsonObject {
            ["id"] = job.Id.ToString(),
            ["kind"] = job.Kind,
            ["name"] = job.Name,
            ["status"] = job.Status.ToString(),
            ["queuedAt"] = job.QueuedAt.ToString("o"),
            ["startedAt"] = job.StartedAt?.ToString("o"),
            ["completedAt"] = job.CompletedAt?.ToString("o"),
            ["error"] = job.Error?.Message,
            ["result"] = job.Result?.ToString()
        };
        var envelope = new JsonObject {
            ["status"] = "ok",
            ["type"] = "jobStatus",
            ["data"] = payload
        };
        return SendResultAsync(writer, id, envelope);
    }

    private Task HandleJobCancelAsync(StreamWriter writer, string? id, JsonNode? @params) {
        if (id is null) return Task.CompletedTask;
        if (_jobManager is null) return SendErrorAsync(writer, id, -32020, "Job manager unavailable");
        var jid = @params?["jobId"]?.ToString();
        if (string.IsNullOrWhiteSpace(jid) || !Guid.TryParse(jid, out var guid)) return SendErrorAsync(writer, id, Err.InvalidParams, "Missing or invalid jobId");
        if (!_jobManager.Cancel(guid)) return SendErrorAsync(writer, id, -32022, "Job not found or cannot cancel");
        var payload = new JsonObject { ["jobId"] = guid.ToString(), ["cancelled"] = true };
        var envelope = new JsonObject {
            ["status"] = "ok",
            ["type"] = "jobCancel",
            ["data"] = payload
        };
        return SendResultAsync(writer, id, envelope);
    }

    private Task HandleDescribeAsync(StreamWriter writer, string? id, JsonNode? @params) {
        if (id is null) return Task.CompletedTask;
        var kind = @params?["kind"]?.ToString(); // request|script
        var name = @params?["name"]?.ToString();
        var workspace = @params?["workspace"]?.ToString();
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(name)) return SendErrorAsync(writer, id, Err.InvalidParams, "Missing kind or name");
        JsonObject? payload = null;
        if (kind.Equals("request", StringComparison.OrdinalIgnoreCase)) {
            // search in specified workspace first (required for disambiguation if duplicate names)
            if (!string.IsNullOrWhiteSpace(workspace)) {
                if (_workspaceService.BaseConfig.Workspaces.TryGetValue(workspace, out var wsDef) && wsDef.Requests.TryGetValue(name, out var reqDef)) {
                    payload = BuildRequestDescribe(workspace, name, reqDef);
                }
            } else {
                // search all workspaces
                foreach (var (wsName, wsDef) in _workspaceService.BaseConfig.Workspaces) {
                    if (wsDef.Requests.TryGetValue(name, out var reqDef)) { payload = BuildRequestDescribe(wsName, name, reqDef); break; }
                }
            }
        } else if (kind.Equals("script", StringComparison.OrdinalIgnoreCase)) {
            if (!string.IsNullOrWhiteSpace(workspace)) {
                if (_workspaceService.BaseConfig.Workspaces.TryGetValue(workspace, out var wsDef) && wsDef.Scripts.TryGetValue(name, out var scriptDef)) {
                    payload = BuildScriptDescribe(workspace, name, scriptDef);
                }
            } else {
                if (_workspaceService.BaseConfig.Scripts.TryGetValue(name, out var globalScript)) {
                    payload = BuildScriptDescribe(null, name, globalScript);
                } else {
                    foreach (var (wsName, wsDef) in _workspaceService.BaseConfig.Workspaces) {
                        if (wsDef.Scripts.TryGetValue(name, out var scriptDef)) { payload = BuildScriptDescribe(wsName, name, scriptDef); break; }
                    }
                }
            }
        } else {
            return SendErrorAsync(writer, id, Err.InvalidParams, "Unsupported kind");
        }
        if (payload is null) return SendErrorAsync(writer, id, -32030, "Not found");
        var envelope = new JsonObject { ["status"] = "ok", ["type"] = "describe", ["data"] = payload };
        return SendResultAsync(writer, id, envelope);

        JsonObject BuildRequestDescribe(string wsName, string reqName, ParksComputing.Api2Cli.Workspace.Models.RequestDefinition def) {
            var argsArr = new JsonArray();
            foreach (var a in def.Arguments.Values) {
                argsArr.Add(new JsonObject {
                    ["name"] = a.Name ?? string.Empty,
                    ["type"] = a.Type ?? string.Empty,
                    ["description"] = a.Description ?? string.Empty,
                    ["required"] = a.IsRequired,
                    ["default"] = a.Default is null ? null : JsonValue.Create(a.Default)
                });
            }
            var headersObj = new JsonObject(); foreach (var h in def.Headers) headersObj[h.Key] = h.Value;
            var cookiesObj = new JsonObject(); foreach (var c in def.Cookies) cookiesObj[c.Key] = c.Value;
            var paramsArr = new JsonArray(); foreach (var p in def.Parameters) paramsArr.Add(p);
            return new JsonObject {
                ["kind"] = "request",
                ["workspace"] = wsName,
                ["name"] = reqName,
                ["description"] = def.Description ?? string.Empty,
                ["method"] = def.Method ?? "GET",
                ["endpoint"] = def.Endpoint ?? string.Empty,
                ["arguments"] = argsArr,
                ["parameters"] = paramsArr,
                ["headers"] = headersObj,
                ["cookies"] = cookiesObj,
                ["hasPayload"] = !string.IsNullOrWhiteSpace(def.Payload)
            };
        }
        JsonObject BuildScriptDescribe(string? wsName, string scriptName, ParksComputing.Api2Cli.Workspace.Models.ScriptDefinition def) {
            var argsArr = new JsonArray();
            foreach (var a in def.Arguments.Values) {
                argsArr.Add(new JsonObject {
                    ["name"] = a.Name ?? string.Empty,
                    ["type"] = a.Type ?? string.Empty,
                    ["description"] = a.Description ?? string.Empty,
                    ["required"] = a.IsRequired,
                    ["default"] = a.Default is null ? null : JsonValue.Create(a.Default)
                });
            }
            var (lang, body) = def.ResolveLanguageAndBody();
            return new JsonObject {
                ["kind"] = "script",
                ["workspace"] = wsName is null ? JsonValue.Create<string?>(null) : wsName,
                ["name"] = scriptName,
                ["description"] = def.Description ?? string.Empty,
                ["language"] = lang,
                ["arguments"] = argsArr,
                ["bodyPreview"] = body.Length > 200 ? body[..200] + "..." : body
            };
        }
    }

    private static Task SendResultAsync(StreamWriter writer, string? id, JsonNode result) {
        if (id is null) return Task.CompletedTask; // notification => ignore
        var obj = new JsonObject {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };
        return writer.WriteLineAsync(obj.ToJsonString());
    }

    private static Task SendErrorAsync(StreamWriter writer, string? id, int code, string message) {
        var obj = new JsonObject {
            ["jsonrpc"] = "2.0",
            ["id"] = id is null ? null : JsonValue.Create(id),
            ["error"] = new JsonObject {
                ["code"] = code,
                ["message"] = message
            }
        };
        return writer.WriteLineAsync(obj.ToJsonString());
    }

    private McpServerStatus BuildStatus(bool checkExternal = false) {
        if (_listener is not null) {
            return new McpServerStatus(true, false, _port, Environment.ProcessId, _startedUtc, null);
        }
        if (checkExternal) {
            var external = ReadLockFile();
            if (external.HasValue && IsProcessAlive(external.Value.ProcessId)) {
                var ex = external.Value;
                return new McpServerStatus(true, true, ex.Port, ex.ProcessId, ex.StartedUtc, null);
            }
        }
        return new McpServerStatus(false, false, null, null, null, null);
    }

    private (int ProcessId, int Port, DateTime StartedUtc)? ReadLockFile() {
        try {
            if (!File.Exists(_lockFilePath)) return null;
            var text = File.ReadAllText(_lockFilePath);
            var doc = JsonSerializer.Deserialize<LockFileModel>(text);
            if (doc is null) return null;
            return (doc.ProcessId, doc.Port, doc.StartedUtc);
    } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[McpServer] read lock file failed: {ex.Message}"); return null; }
    }

    private void WriteLockFile() {
        try {
            var model = new LockFileModel { ProcessId = Environment.ProcessId, Port = _port ?? 0, StartedUtc = _startedUtc ?? DateTime.UtcNow };
            File.WriteAllText(_lockFilePath, JsonSerializer.Serialize(model));
        } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[McpServer] write lock file failed: {ex.Message}"); }
    }

    private void TryDeleteLock() {
    try { if (File.Exists(_lockFilePath)) File.Delete(_lockFilePath); }
    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[McpServer] delete lock file failed: {ex.Message}"); }
    }

    private static bool IsProcessAlive(int pid) {
    try { var p = Process.GetProcessById(pid); return !p.HasExited; }
    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[McpServer] process check failed for {pid}: {ex.Message}"); return false; }
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _ = StopAsync();
    }

    private sealed class LockFileModel { public int ProcessId { get; set; } public int Port { get; set; } public DateTime StartedUtc { get; set; } }
}

public static class McpServiceCollectionExtensions {
    public static IServiceCollection AddMcpServer(this IServiceCollection services) {
        services.AddSingleton<IMcpServerManager, McpServerManager>();
        return services;
    }
}
