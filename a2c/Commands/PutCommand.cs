using System.Collections.Generic;
using System.Net.Http.Headers;

using Cliffer;

using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Cli.Services;

namespace ParksComputing.Api2Cli.Cli.Commands;

[Command("put", "Send resources to the specified API endpoint via a PUT request.")]
[Argument(typeof(string), "payload", "Content to send with the request. If input is redirected, content can also be read from standard input.")]
[Option(typeof(string), "--endpoint", "The endpoint to send the PUT request to.", new[] { "-e" }, IsRequired = false, Arity = ArgumentArity.ExactlyOne)]
[Option(typeof(string), "--baseurl", "The base URL of the API.", new[] { "-b" }, IsRequired = false)]
[Option(typeof(IEnumerable<string>), "--headers", "Headers to include in the request.", new[] { "-h" }, AllowMultipleArgumentsPerToken = true, Arity = ArgumentArity.ZeroOrMore)]
internal class PutCommand(
    A2CApi a2c,
    IConsoleWriter consoleWriter
    )
{
    public string ResponseContent { get; protected set; } = string.Empty;
    public int StatusCode { get; protected set; } = 0;
    public HttpResponseHeaders? Headers { get; protected set; }
    private readonly IConsoleWriter _console = consoleWriter;

    public int Execute(
        [ArgumentParam("payload")] string? payload,
        [OptionParam("--endpoint")] string endpoint,
        [OptionParam("--baseurl")] string? baseUrl,
        [OptionParam("--headers")] IEnumerable<string>? headers
        )
    {
        // Validate URL format
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var baseUri) || string.IsNullOrWhiteSpace(baseUri.Scheme)) {
            baseUrl ??= a2c.ActiveWorkspace.BaseUrl;

            if (string.IsNullOrEmpty(baseUrl) || !Uri.TryCreate(new Uri(baseUrl), endpoint, out baseUri) || string.IsNullOrWhiteSpace(baseUri.Scheme)) {
                _console.WriteError($"{Constants.ErrorChar} Error: Invalid base URL: {baseUrl}", category: "cli.put", code: "baseurl.invalid", ctx: new Dictionary<string, object?> { ["baseUrl"] = baseUrl, ["endpoint"] = endpoint });
                return Result.InvalidArguments;
            }
        }

        baseUrl = baseUri.ToString();

        if (string.IsNullOrEmpty(payload) && Console.IsInputRedirected) {
            var payloadString = Console.In.ReadToEnd();
            payload = payloadString.Trim();
        }

        int result = Result.Success;

        try {
            var response = a2c.Http.Put(baseUrl, payload, headers);

            if (response is null) {
                _console.WriteError($"{Constants.ErrorChar} Error: No response received from {baseUrl}", category: "cli.put", code: "response.none", ctx: new Dictionary<string, object?> { ["baseUrl"] = baseUrl, ["endpoint"] = endpoint });
                result = Result.Error;
            }
            else if (!response.IsSuccessStatusCode) {
                _console.WriteError($"{Constants.ErrorChar} {(int)response.StatusCode} {response.ReasonPhrase} at {baseUrl}", category: "cli.put", code: "http.status.error", ctx: new Dictionary<string, object?> { ["baseUrl"] = baseUrl, ["endpoint"] = endpoint, ["status"] = (int)response.StatusCode, ["reason"] = response.ReasonPhrase });
                result = Result.Error;
            }

            Headers = a2c.Http.Headers;
            ResponseContent = a2c.Http.ResponseContent;
            StatusCode = a2c.Http.StatusCode;
        }
        catch (Exception ex) {
            _console.WriteError($"{Constants.ErrorChar} Error: {ex.Message}", category: "cli.put", code: "http.request.failed", ex: ex, ctx: new Dictionary<string, object?> { ["baseUrl"] = baseUrl, ["endpoint"] = endpoint });
            result = Result.Error;
        }

        return result;
    }
}
