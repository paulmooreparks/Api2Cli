using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

using Cliffer;

using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Cli.Cli.Commands.WorkspaceTools;

[Command("import", "Import an OpenAPI/Swagger document into a new workspace folder (does NOT modify config.xfer)", Parent = "workspace")]
[Option(typeof(string), "--name", "Logical workspace name (used in guidance output)", new[] { "-n" }, IsRequired = true)]
[Option(typeof(string), "--dir", "Target workspace directory to create (relative or absolute)", new[] { "-d" }, IsRequired = false)]
[Option(typeof(string), "--openapi", "Path or URL to an OpenAPI JSON document (YAML not yet supported)", new[] { "-o" }, IsRequired = false)]
[Option(typeof(string), "--baseurl", "Base URL to set; defaults to first server.url or origin", new[] { "-b" }, IsRequired = false)]
[Option(typeof(bool), "--force", "Overwrite existing directory and workspace.xfer", new[] { "-f" }, IsRequired = false)]
[Argument(typeof(string), "source", "Path or URL to an OpenAPI JSON document or a Swagger UI page to auto-discover from.", Arity = Cliffer.ArgumentArity.ZeroOrOne)]
internal class ImportOpenApiCommand(
    IWorkspaceService workspaceService
) : WorkspaceImportCommandBase(workspaceService) {
    public async Task<int> Execute(
        [OptionParam("--name")] string name,
    [OptionParam("--dir")] string? dir,
        [OptionParam("--openapi")] string? openapi,
        [OptionParam("--baseurl")] string? baseurl,
        [OptionParam("--force")] bool force,
        [ArgumentParam("source")] string? source
    ) {
        try {
            var effectiveSource = openapi ?? source;
            if (string.IsNullOrWhiteSpace(effectiveSource)) {
                Console.Error.WriteLine($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Please specify a source URL or file path (positional) or use --openapi.");
                return Result.InvalidArguments;
            }
            dir ??= name; // default directory name from workspace name
            if (!TryResolveTargetDirectory(dir, force, out var targetDir)) { return Result.InvalidArguments; }

            var (content, contentType, finalUri) = await LoadOpenApiAsync(effectiveSource!).ConfigureAwait(false);

            if (!LooksLikeJson(content)) {
                if (IsYamlIndicated(contentType) || LooksLikeYaml(content)) {
                    Console.Error.WriteLine("YAML OpenAPI specs are not yet supported. Please supply a JSON spec.");
                    return Result.InvalidArguments;
                }
            }

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var detectedBaseUrl = baseurl ?? string.Empty;
            if (string.IsNullOrWhiteSpace(detectedBaseUrl) && root.TryGetProperty("servers", out var servers) && servers.ValueKind == JsonValueKind.Array && servers.GetArrayLength() > 0) {
                var first = servers[0];
                if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String) {
                    detectedBaseUrl = urlProp.GetString() ?? string.Empty;
                }
            }
            if (string.IsNullOrWhiteSpace(detectedBaseUrl) && finalUri is not null && (finalUri.Scheme == Uri.UriSchemeHttp || finalUri.Scheme == Uri.UriSchemeHttps)) {
                detectedBaseUrl = new Uri(finalUri, "/").AbsoluteUri;
            }

            var wsDef = new ParksComputing.Api2Cli.Workspace.Models.WorkspaceDefinition {
                Name = name,
                Description = $"Imported from OpenAPI {effectiveSource}",
                BaseUrl = detectedBaseUrl
            };

            if (!root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object) {
                Console.Error.WriteLine($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Invalid OpenAPI document: missing 'paths'.");
                return Result.InvalidArguments;
            }

            foreach (var pathProp in paths.EnumerateObject()) {
                if (pathProp.Value.ValueKind != JsonValueKind.Object) { continue; }
                var path = pathProp.Name;
                foreach (var op in pathProp.Value.EnumerateObject()) {
                    var method = op.Name.ToUpperInvariant();
                    if (!IsHttpMethod(method)) { continue; }
                    var body = op.Value;
                    string? operationId = null;
                    if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("operationId", out var opIdProp) && opIdProp.ValueKind == JsonValueKind.String) {
                        operationId = opIdProp.GetString();
                    }
                    var reqName = WorkspaceImportHelpers.MakeRequestName(method, path, operationId);
                    if (!wsDef.Requests.ContainsKey(reqName)) {
                        wsDef.Requests[reqName] = new ParksComputing.Api2Cli.Workspace.Models.RequestDefinition {
                            Name = reqName,
                            Method = method,
                            Endpoint = path,
                            Description = $"{method} {path}" + (string.IsNullOrWhiteSpace(operationId) ? string.Empty : $" ({operationId})")
                        };
                    }
                }
            }

            var serialized = SerializeWorkspace(wsDef);
            WriteWorkspaceFile(targetDir, serialized);
            EmitActivationGuidance(name, targetDir, wsDef.Requests.Count);
            return Result.Success;
        }
        catch (JsonException jx) {
            Console.Error.WriteLine($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Failed to parse OpenAPI JSON: {jx.Message}");
            return Result.Error;
        }
        catch (Exception ex) {
            Console.Error.WriteLine($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Import failed: {ex.Message}");
            return Result.Error;
        }
    }

    private static bool IsHttpMethod(string method) => method is "GET" or "POST" or "PUT" or "DELETE" or "PATCH" or "HEAD" or "OPTIONS" or "TRACE";

    // Helpers moved to base / shared helpers

    private static async Task<(string Content, string? ContentType, Uri? FinalUri)> LoadOpenApiAsync(string source) {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)) {
            using var http = NewHttpClient();

            var (ok, body, ct) = await TryGetAsync(http, uri).ConfigureAwait(false);
            if (ok && (IsJsonContentType(ct) || (body is not null && LooksLikeJson(body)))) {
                return (body!, ct, uri);
            }

            if (ok && LooksLikeHtml(body!)) {
                var discovered = TryExtractSpecUrlFromSwaggerUiHtml(body!, uri);
                if (discovered is not null) {
                    var (ok2, body2, ct2) = await TryGetAsync(http, discovered).ConfigureAwait(false);
                    if (ok2 && (IsJsonContentType(ct2) || (body2 is not null && LooksLikeJson(body2)))) {
                        return (body2!, ct2, discovered);
                    }
                    if (ok2 && (IsYamlContentType(ct2) || (body2 is not null && LooksLikeYaml(body2)))) {
                        throw new InvalidOperationException("YAML OpenAPI specs are not yet supported. Please supply a JSON spec.");
                    }
                }
            }

            foreach (var candidate in BuildWellKnownCandidates(uri)) {
                var (ok3, body3, ct3) = await TryGetAsync(http, candidate).ConfigureAwait(false);
                if (!ok3) { continue; }
                if (IsJsonContentType(ct3) || (body3 is not null && LooksLikeJson(body3))) {
                    return (body3!, ct3, candidate);
                }
            }

            if (ok && !string.IsNullOrWhiteSpace(body)) {
                return (body!, ct, uri);
            }

            throw new InvalidOperationException($"Failed to locate an OpenAPI JSON document from '{source}'. Try passing a direct JSON URL or file path.");
        }
        else {
            if (!File.Exists(source)) {
                throw new FileNotFoundException($"File not found: {source}");
            }
            var text = File.ReadAllText(source);
            return (text, null, null);
        }
    }

    private static HttpClient NewHttpClient() {
        var http = new HttpClient(new HttpClientHandler {
            AllowAutoRedirect = true
        }) {
            Timeout = TimeSpan.FromSeconds(15)
        };
        return http;
    }

    private static async Task<(bool Ok, string? Body, string? ContentType)> TryGetAsync(HttpClient http, Uri uri) {
        try {
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) {
                return (false, null, null);
            }
            var ct = resp.Content.Headers.ContentType?.MediaType;
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return (true, body, ct);
        }
        catch (Exception ex) {
            if (IsScriptDebugEnabled()) { try { Console.Error.WriteLine($"[ImportOpenApi] GET {uri} failed :: {ex.GetType().Name}: {ex.Message}"); } catch { } }
            return (false, null, null);
        }
    }

    private static bool IsScriptDebugEnabled() => string.Equals(Environment.GetEnvironmentVariable("A2C_SCRIPT_DEBUG"), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable("A2C_SCRIPT_DEBUG"), "1", StringComparison.OrdinalIgnoreCase);

    private static bool IsJsonContentType(string? ct)
        => !string.IsNullOrWhiteSpace(ct) && (ct!.Contains("json", StringComparison.OrdinalIgnoreCase) || ct!.Equals("application/vnd.oai.openapi+json", StringComparison.OrdinalIgnoreCase));

    private static bool IsYamlContentType(string? ct)
        => !string.IsNullOrWhiteSpace(ct) && ct!.Contains("yaml", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<Uri> BuildWellKnownCandidates(Uri seed) {
        var origin = new Uri(seed.GetLeftPart(UriPartial.Authority));
        var dir = seed.AbsolutePath.EndsWith("/") ? seed : new Uri(seed, ".");
        var bases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            origin.AbsoluteUri.TrimEnd('/') + "/",
            new Uri(seed, "/").AbsoluteUri,
            new Uri(dir, "/").AbsoluteUri
        };

        var wellKnown = new[] {
            "swagger/v1/swagger.json",
            "swagger.json",
            "openapi.json",
            "v3/api-docs",
            "spec.json",
            "api-docs",
            "swagger/v1/openapi.json",
            "openapi/v1.json"
        };

        foreach (var b in bases) {
            foreach (var path in wellKnown) {
                yield return new Uri(new Uri(b), path);
            }
        }
    }

    private static bool LooksLikeHtml(string content) {
        if (string.IsNullOrWhiteSpace(content)) { return false; }
        var head = content.Length > 2048 ? content[..2048] : content;
        var t = head.TrimStart();
        return t.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
               t.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("SwaggerUI", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("Swagger UI", StringComparison.OrdinalIgnoreCase);
    }

    private static Uri? TryExtractSpecUrlFromSwaggerUiHtml(string html, Uri pageUri) {
        if (string.IsNullOrWhiteSpace(html)) { return null; }
        var patterns = new[] {
            new Regex("SwaggerUI(?:Bundle)?\\s*\\(\\s*\\{[\\s\\S]*?url\\s*:\\s*(['\"`])(?<u>[^'\"`]+)\\1", RegexOptions.IgnoreCase),
            new Regex("urls\\s*:\\s*\\[\\s*\\{[\\s\\S]*?url\\s*:\\s*(['\"`])(?<u>[^'\"`]+)\\1", RegexOptions.IgnoreCase),
            new Regex("configUrl\\s*:\\s*(['\"`])(?<cfg>[^'\"`]+)\\1", RegexOptions.IgnoreCase)
        };

        foreach (var re in patterns) {
            var m = re.Match(html);
            if (!m.Success) { continue; }
            if (m.Groups["u"].Success) {
                var raw = m.Groups["u"].Value.Trim();
                if (Uri.TryCreate(pageUri, raw, out var abs)) { return abs; }
            }
            if (m.Groups["cfg"].Success) {
                var cfgRaw = m.Groups["cfg"].Value.Trim();
                if (Uri.TryCreate(pageUri, cfgRaw, out var cfgAbs)) { return cfgAbs; }
            }
        }
        return null;
    }

    private static bool LooksLikeJson(string content) {
        if (content is null) { return false; }
        foreach (var ch in content) {
            if (char.IsWhiteSpace(ch)) { continue; }
            return ch == '{' || ch == '['; // minimal heuristic
        }
        return false;
    }

    private static bool LooksLikeYaml(string content) {
        if (string.IsNullOrWhiteSpace(content)) { return false; }
        var head = content.Length > 2048 ? content[..2048] : content;
        if (head.TrimStart().StartsWith("---")) { return true; }
        var re = new Regex(@"^\s*(openapi|swagger)\s*:\s*\S+", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return re.IsMatch(head) && !LooksLikeJson(head);
    }

    private static bool IsYamlIndicated(string? contentType)
        => !string.IsNullOrWhiteSpace(contentType) &&
           (contentType!.Contains("yaml", StringComparison.OrdinalIgnoreCase) ||
            contentType!.Equals("text/x-yaml", StringComparison.OrdinalIgnoreCase));
}
