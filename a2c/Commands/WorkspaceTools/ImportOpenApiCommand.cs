using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

using Cliffer;

using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Cli.Cli.Commands.WorkspaceTools;

[Command("import", "Create a workspace from an OpenAPI/Swagger document (JSON only)", Parent = "workspace")]
[Option(typeof(string), "--name", "Name for the new workspace", new[] { "-n" }, IsRequired = true)]
[Option(typeof(string), "--openapi", "Path or URL to an OpenAPI JSON document (YAML not yet supported)", new[] { "-o" }, IsRequired = false)]
[Option(typeof(string), "--baseurl", "Base URL to set for the workspace; defaults to first server.url in the spec if omitted", new[] { "-b" }, IsRequired = false)]
[Option(typeof(bool), "--force", "Overwrite existing workspace if it already exists", new[] { "-f" }, IsRequired = false)]
[Argument(typeof(string), "source", "Path or URL to an OpenAPI JSON document or a Swagger UI page to auto-discover from.", Arity = Cliffer.ArgumentArity.ZeroOrOne)]
internal class ImportOpenApiCommand(
    IWorkspaceService workspaceService
) {
    private readonly IWorkspaceService _workspaceService = workspaceService;
    public async Task<int> Execute(
        [OptionParam("--name")] string name,
        [OptionParam("--openapi")] string? openapi,
        [OptionParam("--baseurl")] string? baseurl,
        [OptionParam("--force")] bool force,
        [ArgumentParam("source")] string? source
    ) {
        try {
            // Allow positional source or --openapi; require at least one
            var effectiveSource = openapi ?? source;
            if (string.IsNullOrWhiteSpace(effectiveSource)) {
                Console.Error.WriteLine($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Please specify a source URL or file path (positional) or use --openapi.");
                return Result.InvalidArguments;
            }
            var ws = _workspaceService;
            if (!force && ws.BaseConfig.Workspaces.ContainsKey(name)) {
                Console.Error.WriteLine($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Workspace '{name}' already exists. Use --force to overwrite.");
                return Result.InvalidArguments;
            }

            // Load the document from either URL or file path, preferring robust detection and auto-discovery
            var (content, contentType, finalUri) = await LoadOpenApiAsync(effectiveSource!).ConfigureAwait(false);

            // Prefer content-based detection over file extensions
            if (!LooksLikeJson(content)) {
                if (IsYamlIndicated(contentType) || LooksLikeYaml(content)) {
                    Console.Error.WriteLine("YAML OpenAPI specs are not yet supported. Please supply a JSON spec.");
                    return Result.InvalidArguments;
                }
                // Fall-through: try parsing as JSON anyway for resilience; if it fails, user gets a precise parse error below
            }

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // Base URL: servers[0].url
            var detectedBaseUrl = baseurl ?? string.Empty;
            if (string.IsNullOrWhiteSpace(detectedBaseUrl) && root.TryGetProperty("servers", out var servers) && servers.ValueKind == JsonValueKind.Array && servers.GetArrayLength() > 0) {
                var first = servers[0];
                if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String) {
                    detectedBaseUrl = urlProp.GetString() ?? string.Empty;
                }
            }
            // If servers[] was missing or empty, fall back to the origin of the discovered spec URL
            if (string.IsNullOrWhiteSpace(detectedBaseUrl) && finalUri is not null && (finalUri.Scheme == Uri.UriSchemeHttp || finalUri.Scheme == Uri.UriSchemeHttps)) {
                detectedBaseUrl = new Uri(finalUri, "/").AbsoluteUri; // origin with trailing slash
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
                var path = pathProp.Name;
                if (pathProp.Value.ValueKind != JsonValueKind.Object) { continue; }
                foreach (var methodProp in pathProp.Value.EnumerateObject()) {
                    var method = methodProp.Name.ToUpperInvariant();
                    if (!IsHttpMethod(method)) { continue; }
                    string? operationId = null;
                    string? summary = null;
                    if (methodProp.Value.ValueKind == JsonValueKind.Object) {
                        if (methodProp.Value.TryGetProperty("operationId", out var opId) && opId.ValueKind == JsonValueKind.String) {
                            operationId = opId.GetString();
                        }
                        if (methodProp.Value.TryGetProperty("summary", out var sum) && sum.ValueKind == JsonValueKind.String) {
                            summary = sum.GetString();
                        }
                    }

                    var reqName = MakeRequestName(method, path, operationId);
                    var reqDef = new ParksComputing.Api2Cli.Workspace.Models.RequestDefinition {
                        Name = reqName,
                        Description = summary ?? $"{method} {path}",
                        Endpoint = path,
                        Method = method,
                    };
                    wsDef.Requests[reqName] = reqDef;
                }
            }

            ws.BaseConfig.Workspaces[name] = wsDef;
            ws.SaveConfig();
            Console.WriteLine($"Workspace '{name}' created with {wsDef.Requests.Count} requests. Run 'reload' to activate it.");
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

    private static string MakeRequestName(string method, string path, string? operationId) {
        if (!string.IsNullOrWhiteSpace(operationId)) {
            return operationId;
        }
        var p = (path ?? string.Empty).Trim();
        if (p.StartsWith("/")) { p = p.Substring(1); }
        var chars = p.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var baseName = new string(chars);
        if (string.IsNullOrWhiteSpace(baseName)) {
            baseName = "root";
        }
        return $"{method.ToLowerInvariant()}_{baseName}";
    }

    private static async Task<(string Content, string? ContentType, Uri? FinalUri)> LoadOpenApiAsync(string source) {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)) {
            using var http = NewHttpClient();

            // Try the provided URL first
            var (ok, body, ct) = await TryGetAsync(http, uri).ConfigureAwait(false);
            if (ok && (IsJsonContentType(ct) || (body is not null && LooksLikeJson(body)))) {
                return (body!, ct, uri);
            }

            // If it looks like a Swagger UI page (HTML), attempt to extract the spec URL from the page
            if (ok && LooksLikeHtml(body!)) {
                var discovered = TryExtractSpecUrlFromSwaggerUiHtml(body!, uri);
                if (discovered is not null) {
                    var (ok2, body2, ct2) = await TryGetAsync(http, discovered).ConfigureAwait(false);
                    if (ok2 && (IsJsonContentType(ct2) || (body2 is not null && LooksLikeJson(body2)))) {
                        return (body2!, ct2, discovered);
                    }
                    // If YAML is indicated, surface the unsupported message clearly
                    if (ok2 && (IsYamlContentType(ct2) || (body2 is not null && LooksLikeYaml(body2)))) {
                        throw new InvalidOperationException("YAML OpenAPI specs are not yet supported. Please supply a JSON spec.");
                    }
                }
            }

            // Not HTML or initial fetch failed; try common well-known endpoints relative to the origin and path
            foreach (var candidate in BuildWellKnownCandidates(uri)) {
                var (ok3, body3, ct3) = await TryGetAsync(http, candidate).ConfigureAwait(false);
                if (!ok3) { continue; }
                if (IsJsonContentType(ct3) || (body3 is not null && LooksLikeJson(body3))) {
                    return (body3!, ct3, candidate);
                }
            }

            // As a last resort, if initial fetch succeeded but wasn't clearly JSON, return it and let JSON parse error surface
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
            // No reliable content-type for files; return null and rely on content sniffing
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
        catch {
            return (false, null, null);
        }
    }

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
        // Patterns commonly used by Swagger UI initializers
        var patterns = new[] {
            // SwaggerUIBundle({ url: "..." })
            new Regex("SwaggerUI(?:Bundle)?\\s*\\(\\s*\\{[\\s\\S]*?url\\s*:\\s*(['\"`])(?<u>[^'\"`]+)\\1", RegexOptions.IgnoreCase),
            // urls: [ { url: "..." } ]
            new Regex("urls\\s*:\\s*\\[\\s*\\{[\\s\\S]*?url\\s*:\\s*(['\"`])(?<u>[^'\"`]+)\\1", RegexOptions.IgnoreCase),
            // configUrl: '/v3/api-docs/swagger-config' sometimes used; follow that to find urls[]
            new Regex("configUrl\\s*:\\s*(['\"`])(?<cfg>[^'\"`]+)\\1", RegexOptions.IgnoreCase)
        };

        foreach (var re in patterns) {
            var m = re.Match(html);
            if (m.Success) {
                if (m.Groups["u"].Success) {
                    var raw = m.Groups["u"].Value;
                    return new Uri(pageUri, raw);
                }
                if (m.Groups["cfg"].Success) {
                    // Attempt to fetch swagger-config and read urls/url
                    try {
                        using var http = NewHttpClient();
                        var cfgUri = new Uri(pageUri, m.Groups["cfg"].Value);
                        var task = TryGetAsync(http, cfgUri);
                        task.Wait();
                        var (ok, body, _) = task.Result;
                        if (ok && !string.IsNullOrWhiteSpace(body)) {
                            using var doc = JsonDocument.Parse(body);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("urls", out var urls) && urls.ValueKind == JsonValueKind.Array && urls.GetArrayLength() > 0) {
                                var first = urls[0];
                                if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String) {
                                    return new Uri(pageUri, urlProp.GetString()!);
                                }
                            }
                            if (root.TryGetProperty("url", out var urlSingle) && urlSingle.ValueKind == JsonValueKind.String) {
                                return new Uri(pageUri, urlSingle.GetString()!);
                            }
                        }
                    }
                    catch { /* ignore and continue */ }
                }
            }
        }
        return null;
    }

    private static bool LooksLikeJson(string content) {
        if (content is null) { return false; }
        foreach (var ch in content) {
            if (char.IsWhiteSpace(ch)) { continue; }
            return ch == '{' || ch == '['; // quick heuristic
        }
        return false;
    }

    private static bool LooksLikeYaml(string content) {
        if (string.IsNullOrWhiteSpace(content)) { return false; }
        // Heuristics: leading '---', or top-level keys like 'openapi:'/'swagger:' near the start and not JSON braces
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
