using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http;

using Cliffer;

using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Cli.Services;

namespace ParksComputing.Api2Cli.Cli.Commands;

[Command("delete", "Send a DELETE request to the specified API endpoint.")]
[Option(typeof(string), "--endpoint", "The endpoint to send the DELETE request to.", new[] { "-e" }, IsRequired = false, Arity = ArgumentArity.ExactlyOne)]
[Option(typeof(string), "--baseurl", "The base URL of the API.", new[] { "-b" }, IsRequired = false)]
[Option(typeof(IEnumerable<string>), "--headers", "Headers to include in the request.", new[] { "-h" }, AllowMultipleArgumentsPerToken = true, Arity = ArgumentArity.ZeroOrMore)]
internal class DeleteCommand(
    A2CApi a2c,
    IConsoleWriter consoleWriter
    ) {
    public string ResponseContent { get; protected set; } = string.Empty;
    public int StatusCode { get; protected set; } = 0;
    public HttpResponseHeaders? Headers { get; protected set; }
    private readonly IConsoleWriter _console = consoleWriter;

    public int Execute(
        [OptionParam("--endpoint")] string endpoint,
        [OptionParam("--baseurl")] string? baseUrl,
        [OptionParam("--headers")] IEnumerable<string>? headers
    ) {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var fullUri) || string.IsNullOrWhiteSpace(fullUri.Scheme)) {
            baseUrl ??= a2c.ActiveWorkspace.BaseUrl;

            if (string.IsNullOrEmpty(baseUrl) || !Uri.TryCreate(new Uri(baseUrl), endpoint, out fullUri) || string.IsNullOrWhiteSpace(fullUri.Scheme)) {
                _console.WriteError($"{Constants.ErrorChar} Error: Invalid base URL: {baseUrl}", category: "cli.delete", code: "baseurl.invalid", ctx: new Dictionary<string, object?> { ["baseUrl"] = baseUrl, ["endpoint"] = endpoint });
                return Result.InvalidArguments;
            }
        }

        int result = Result.Success;

        try {
            var response = a2c.Http.Delete(fullUri.ToString(), headers);

            if (response is null) {
                _console.WriteError($"{Constants.ErrorChar} Error: No response received from {fullUri}", category: "cli.delete", code: "response.none", ctx: new Dictionary<string, object?> { ["url"] = fullUri.ToString() });
                result = Result.Error;
            }
            else if (!response.IsSuccessStatusCode) {
                _console.WriteError($"{Constants.ErrorChar} {(int)response.StatusCode} {response.ReasonPhrase} at {fullUri}", category: "cli.delete", code: "http.status.error", ctx: new Dictionary<string, object?> { ["url"] = fullUri.ToString(), ["status"] = (int)response.StatusCode, ["reason"] = response.ReasonPhrase });
                result = Result.Error;
            }

            Headers = a2c.Http.Headers;
            ResponseContent = a2c.Http.ResponseContent;
            StatusCode = a2c.Http.StatusCode;
        }
        catch (HttpRequestException ex) {
            _console.WriteError($"{Constants.ErrorChar} Error: HTTP request failed - {ex.Message}", category: "cli.delete", code: "http.request.failed", ex: ex, ctx: new Dictionary<string, object?> { ["url"] = fullUri.ToString() });
            result = Result.Error;
        }
        catch (Exception ex) {
            _console.WriteError($"{Constants.ErrorChar} Error: {ex.Message}", category: "cli.delete", code: "unexpected", ex: ex, ctx: new Dictionary<string, object?> { ["url"] = fullUri.ToString() });
            result = Result.Error;
        }

        return result;
    }
}
