using System.Net.Sockets;
using System.Text;

// a2c.mcp.bridge
// Simple transparent stdio <-> TCP relay for the a2c MCP server.
// - Passes raw bytes (no framing transformation)
// - Supports automatic (re)connection attempts
// - Exits when either stdin closes or remote socket closes
// Usage examples:
//   dotnet run --project a2c.mcp.bridge -- --port 61658
//   A2C_MCP_PORT=61658 a2c.mcp.bridge
// Arguments:
//   --port <int> (or env A2C_MCP_PORT)  default: 61658
//   --host <string> (or env A2C_MCP_HOST) default: 127.0.0.1
//   --retries <int> reconnect attempts if initial connect fails (default 30)
//   --retry-delay <ms> delay between attempts (default 1000)

await BridgeMain(args); // ignore exit code at top-level; process will exit after tasks complete

static async Task<int> BridgeMain(string[] args) {
    int port = 61658;
    string host = Environment.GetEnvironmentVariable("A2C_MCP_HOST") ?? "127.0.0.1";
    int retries = 30;
    int retryDelayMs = 1000;

    for (int i = 0; i < args.Length; i++) {
        switch (args[i]) {
            case "--port": if (i + 1 < args.Length && int.TryParse(args[++i], out var p)) port = p; break;
            case "--host": if (i + 1 < args.Length) host = args[++i]; break;
            case "--retries": if (i + 1 < args.Length && int.TryParse(args[++i], out var r)) retries = r; break;
            case "--retry-delay": if (i + 1 < args.Length && int.TryParse(args[++i], out var d)) retryDelayMs = d; break;
            case "--help": case "-h": PrintHelp(); return 0;
        }
    }
    if (Environment.GetEnvironmentVariable("A2C_MCP_PORT") is string envPort && int.TryParse(envPort, out var ep)) port = ep;

    Console.Error.WriteLine($"[a2c.mcp.bridge] connecting to tcp://{host}:{port} (retries={retries}, delay={retryDelayMs}ms)");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    TcpClient? client = null;
    for (int attempt = 0; attempt <= retries && !cts.IsCancellationRequested; attempt++) {
        try {
            client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var completed = await Task.WhenAny(connectTask, Task.Delay(-1, timeoutCts.Token));
            if (completed != connectTask) throw new TimeoutException("Connect timeout");
            if (!client.Connected) throw new Exception("Unknown connect failure");
            Console.Error.WriteLine("[a2c.mcp.bridge] connected");
            break;
        } catch (Exception ex) {
            client?.Dispose(); client = null;
            if (attempt == retries) { Console.Error.WriteLine($"[a2c.mcp.bridge] failed: {ex.Message}"); return 2; }
            Console.Error.WriteLine($"[a2c.mcp.bridge] connect attempt {attempt+1} failed: {ex.Message}");
            try { await Task.Delay(retryDelayMs, cts.Token); }
            catch (Exception dlex) {
                if (Environment.GetEnvironmentVariable("A2C_BRIDGE_DEBUG") is string dbg && (dbg == "1" || dbg.Equals("true", StringComparison.OrdinalIgnoreCase))) {
                    Console.Error.WriteLine($"[a2c.mcp.bridge] retry delay interrupted: {dlex.Message}");
                }
            }
        }
    }
    if (client == null) { return 2; }

    using var tcp = client;
    using var netStream = tcp.GetStream();
    var stdin = Console.OpenStandardInput();
    var stdout = Console.OpenStandardOutput();

    var pumpSocket = Task.Run(async () => {
        var buffer = new byte[8192];
        try {
            while (!cts.IsCancellationRequested) {
                var n = await netStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);
                if (n == 0) { break; }
                await stdout.WriteAsync(buffer.AsMemory(0, n), cts.Token);
                await stdout.FlushAsync(cts.Token);
            }
        } catch (OperationCanceledException) { }
        catch (Exception ex) { Console.Error.WriteLine($"[a2c.mcp.bridge] socket read error: {ex.Message}"); }
        cts.Cancel();
    });

    var pumpStdin = Task.Run(async () => {
        var buffer = new byte[8192];
        try {
            while (!cts.IsCancellationRequested) {
                var n = await stdin.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);
                if (n == 0) { break; }
                await netStream.WriteAsync(buffer.AsMemory(0, n), cts.Token);
                await netStream.FlushAsync(cts.Token);
            }
        } catch (OperationCanceledException) { }
        catch (Exception ex) { Console.Error.WriteLine($"[a2c.mcp.bridge] stdin read error: {ex.Message}"); }
        cts.Cancel();
    });

    await Task.WhenAll(pumpSocket, pumpStdin);
    Console.Error.WriteLine("[a2c.mcp.bridge] exiting");
    return 0;
}

static void PrintHelp() {
    Console.WriteLine("a2c.mcp.bridge - stdio <-> TCP bridge for a2c MCP");
    Console.WriteLine("Options:");
    Console.WriteLine("  --port <n>         Port of running a2c MCP (env A2C_MCP_PORT)");
    Console.WriteLine("  --host <addr>      Host (default 127.0.0.1 / env A2C_MCP_HOST)");
    Console.WriteLine("  --retries <n>      Connection retries (default 30)");
    Console.WriteLine("  --retry-delay <ms> Delay between retries (default 1000)");
}
