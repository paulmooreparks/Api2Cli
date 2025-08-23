using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Cliffer;

using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Api2Cli.Http.Services;
using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Cli.Services;

namespace ParksComputing.Api2Cli.Cli.Commands;

[Command("post", "Send resources to the specified API endpoint via a POST request.")]
[Argument(typeof(string), "payload", "Content to send with the request. If input is redirected, content can also be read from standard input.")]
[Option(typeof(string), "--endpoint", "The endpoint to send the POST request to.", new[] { "-e" }, IsRequired = false, Arity = ArgumentArity.ExactlyOne)]
[Option(typeof(string), "--baseurl", "The base URL of the API to send HTTP requests to.", new[] { "-b" }, IsRequired = false)]
[Option(typeof(IEnumerable<string>), "--headers", "Headers to include in the request.", new[] { "-h" }, AllowMultipleArgumentsPerToken = true, Arity = ArgumentArity.ZeroOrMore)]
internal class PostCommand(
    A2CApi a2c,
    IConsoleWriter consoleWriter
    )
{
    public string ResponseContent { get; protected set; } = string.Empty;
    public int StatusCode { get; protected set; } = 0;
    public System.Net.Http.Headers.HttpResponseHeaders? Headers { get; protected set; } = default;
    private readonly IConsoleWriter _console = consoleWriter;

    public int Execute(
        [ArgumentParam("payload")] string? payload,
        [OptionParam("--endpoint")] string? endpoint,
        [OptionParam("--baseurl")] string? baseUrl,
        [OptionParam("--headers")] IEnumerable<string> headers
        )
    {
        // Validate URL format
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var baseUri) || string.IsNullOrWhiteSpace(baseUri.Scheme)) {
            baseUrl ??= a2c.ActiveWorkspace.BaseUrl;

            if (string.IsNullOrEmpty(baseUrl) || !Uri.TryCreate(new Uri(baseUrl), endpoint, out baseUri) || string.IsNullOrWhiteSpace(baseUri.Scheme)) {
                _console.WriteError($"{Constants.ErrorChar} Error: Invalid base URL: {baseUrl}", category: "cli.post", code: "baseurl.invalid", ctx: new Dictionary<string, object?> { ["baseUrl"] = baseUrl, ["endpoint"] = endpoint });
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
            var response = a2c.Http.Post(baseUrl, payload, headers);

            if (response is null) {
                _console.WriteError($"{Constants.ErrorChar} Error: No response received from {baseUrl}", category: "cli.post", code: "response.none", ctx: new Dictionary<string, object?> { ["baseUrl"] = baseUrl, ["endpoint"] = endpoint });
                result = Result.Error;
            }
            else if (!response.IsSuccessStatusCode) {
                _console.WriteError($"{Constants.ErrorChar} {(int)response.StatusCode} {response.ReasonPhrase} at {baseUrl}", category: "cli.post", code: "http.status.error", ctx: new Dictionary<string, object?> { ["baseUrl"] = baseUrl, ["endpoint"] = endpoint, ["status"] = (int)response.StatusCode, ["reason"] = response.ReasonPhrase });
                result = Result.Error;
            }

            Headers = a2c.Http.Headers;
            ResponseContent = a2c.Http.ResponseContent;
            StatusCode = a2c.Http.StatusCode;
            // List<Cookie> responseCookies = cookieContainer.GetCookies(baseUri).Cast<Cookie>().ToList();
        }
        catch (HttpRequestException ex) {
            _console.WriteError($"{Constants.ErrorChar} Error: HTTP request failed - {ex.Message}", category: "cli.post", code: "http.request.failed", ex: ex, ctx: new Dictionary<string, object?> { ["baseUrl"] = baseUrl, ["endpoint"] = endpoint });
            return Result.Error;
        }

        return result;
    }
}
