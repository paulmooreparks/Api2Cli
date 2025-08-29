using Cliffer;
using System.CommandLine;
using System.CommandLine.Invocation;
using ParksComputing.Api2Cli.Cli.Services;
using ParksComputing.Api2Cli.Runtime.Services.Mcp;

namespace ParksComputing.Api2Cli.Cli.Commands.Mcp;

[Command("mcp", "Manage the MCP (Model Context Protocol) server")]
internal class McpCommand {
    public int Execute(Command cmd, InvocationContext ctx) => Result.Success; // help only
}

[Command("status", "Show MCP server status", Parent = "mcp")]
internal class McpStatusCommand {
    private readonly IMcpServerManager _mgr; private readonly IConsoleWriter _console;
    public McpStatusCommand(IMcpServerManager mgr, IConsoleWriter console) { _mgr = mgr; _console = console; }
    public int Execute() {
        var st = _mgr.Refresh();
        var ctx = new Dictionary<string, object?> { ["running"] = st.IsRunning, ["external"] = st.IsExternal, ["port"] = st.Port, ["pid"] = st.ProcessId, ["startedUtc"] = st.StartedUtc };
        _console.WriteLineKey(st.IsRunning?"mcp.status.running":"mcp.status.stopped", "cli.mcp", code: "mcp.status", ctx: ctx);
        return Result.Success;
    }
}

[Command("start", "Start MCP server if not already running", Parent = "mcp")]
[Option(typeof(int), "--port", "Preferred port (0 for auto)", new[] { "-p" }, IsRequired = false)]
internal class McpStartCommand {
    private readonly IMcpServerManager _mgr; private readonly IConsoleWriter _console;
    public McpStartCommand(IMcpServerManager mgr, IConsoleWriter console) { _mgr = mgr; _console = console; }
    public async Task<int> Execute([OptionParam("--port")] int? port, InvocationContext context) {
        var st = await _mgr.StartAsync(port.GetValueOrDefault(0), context.GetCancellationToken());
        var endpoints = st.Port.HasValue
            ? new [] { $"127.0.0.1:{st.Port}", $"localhost:{st.Port}" }
            : Array.Empty<string>();
        var ctx = new Dictionary<string, object?> { ["running"] = st.IsRunning, ["external"] = st.IsExternal, ["port"] = st.Port, ["pid"] = st.ProcessId, ["startedUtc"] = st.StartedUtc, ["endpoints"] = endpoints };
        if (st.IsRunning && st.Port.HasValue) {
            _console.WriteLine($"MCP listening on tcp://127.0.0.1:{st.Port} (aliases: {string.Join(", ", endpoints)})", category: "cli.mcp", code: "mcp.start.endpoint", ctx: ctx);
        }
        _console.WriteLineKey(st.IsExternal?"mcp.start.external":"mcp.start.started", "cli.mcp", code: "mcp.start", ctx: ctx);
        return st.IsRunning ? Result.Success : Result.Error;
    }
}

[Command("stop", "Stop MCP server if this instance started it", Parent = "mcp")]
internal class McpStopCommand {
    private readonly IMcpServerManager _mgr; private readonly IConsoleWriter _console;
    public McpStopCommand(IMcpServerManager mgr, IConsoleWriter console) { _mgr = mgr; _console = console; }
    public async Task<int> Execute(InvocationContext context) {
        var status = _mgr.Status;
        if (!status.IsRunning) {
            _console.WriteLineKey("mcp.stop.notRunning", "cli.mcp", code: "mcp.stop.notRunning");
            return Result.Success;
        }
        if (status.IsExternal) {
            _console.WriteLineKey("mcp.stop.externalRefused", "cli.mcp", code: "mcp.stop.externalRefused", ctx: new Dictionary<string, object?> { ["pid"] = status.ProcessId });
            return Result.Success; // not an error; just not ours
        }
        await _mgr.StopAsync(context.GetCancellationToken());
        _console.WriteLineKey("mcp.stop.stopped", "cli.mcp", code: "mcp.stop.stopped");
        return Result.Success;
    }
}
