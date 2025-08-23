using Cliffer;

using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Cli.Services;

namespace ParksComputing.Api2Cli.Cli.Commands;

[Command("get", "Retrieve resources from the specified API endpoint via a GET request.")]
[Option(typeof(string), "--endpoint", "The endpoint to send the GET request to.", new[] { "-e" }, IsRequired = false, Arity = ArgumentArity.ExactlyOne)]
[Option(typeof(string), "--baseurl", "The base URL of the API to send HTTP requests to.", new[] { "-b" }, IsRequired = false)]
[Option(typeof(IEnumerable<string>), "--parameters", "Query parameters to include in the request. If input is redirected, parameters can also be read from standard input.", new[] { "-p" }, AllowMultipleArgumentsPerToken = true, Arity = ArgumentArity.ZeroOrMore)]
[Option(typeof(IEnumerable<string>), "--headers", "Headers to include in the request.", new[] { "-h" }, AllowMultipleArgumentsPerToken = true, Arity = ArgumentArity.ZeroOrMore)]
[Option(typeof(IEnumerable<string>), "--cookies", "Cookies to include in the request.", new[] { "-c" }, AllowMultipleArgumentsPerToken = true, Arity = ArgumentArity.ZeroOrMore)]
[Option(typeof(bool), "--quiet", "If true, suppress echo of the response to the console.", new[] { "-q" }, Arity = ArgumentArity.ZeroOrOne, IsRequired = false)]
internal class GetCommand(
    A2CApi a2c,
    IConsoleWriter consoleWriter
    ) {
    public string ResponseContent { get; protected set; } = string.Empty;
    public int StatusCode { get; protected set; } = 0;
    public System.Net.Http.Headers.HttpResponseHeaders? Headers { get; protected set; } = default;
    private readonly IConsoleWriter _console = consoleWriter;

    public int Execute(
        [OptionParam("--endpoint")] string endpoint,
        [OptionParam("--baseurl")] string? baseUrl,
        [OptionParam("--parameters")] IEnumerable<string> parameters,
        [OptionParam("--headers")] IEnumerable<string> headers,
        [OptionParam("--cookies")] IEnumerable<string> cookies,
        [OptionParam("--quiet")] bool isQuiet = true
        )
    {
        // Validate URL format
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var baseUri) || string.IsNullOrWhiteSpace(baseUri.Scheme)) {
            baseUrl ??= a2c.ActiveWorkspace.BaseUrl;

            if (string.IsNullOrEmpty(baseUrl) || !Uri.TryCreate(new Uri(baseUrl), endpoint, out baseUri) || string.IsNullOrWhiteSpace(baseUri.Scheme)) {
                _console.WriteError($"{Constants.ErrorChar} Error: Invalid base URL: {baseUrl}", category: "cli.get", code: "baseurl.invalid", ctx: new Dictionary<string, object?> { ["baseUrl"] = baseUrl, ["endpoint"] = endpoint });
                return Result.InvalidArguments;
            }
        }

        baseUrl = baseUri.ToString();
        var paramList = new List<string>();

        if (parameters is not null) {
            paramList.AddRange(parameters!);
        }
        else if (Console.IsInputRedirected) {
            var paramString = Console.In.ReadToEnd();
            paramString = paramString.Trim();
            var inputParams = paramString.Split(' ', StringSplitOptions.None);

            foreach (var param in inputParams) {
                paramList.Add(param);
            }
        }

        int result = Result.Success;

        try {
            var response = a2c.Http.Get(baseUrl, paramList, headers);

            if (response is null) {
                _console.WriteError($"{Constants.ErrorChar} Error: No response received from {baseUrl}", category: "cli.get", code: "response.none", ctx: new Dictionary<string, object?> { ["baseUrl"] = baseUrl, ["endpoint"] = endpoint });
                result = Result.Error;
            }
            else if (!response.IsSuccessStatusCode) {
                _console.WriteError($"{Constants.ErrorChar} {(int) response.StatusCode} {response.ReasonPhrase} at {baseUrl}", category: "cli.get", code: "http.status.error", ctx: new Dictionary<string, object?> { ["baseUrl"] = baseUrl, ["endpoint"] = endpoint, ["status"] = (int) response.StatusCode, ["reason"] = response.ReasonPhrase });
                result = Result.Error;
            }

            Headers = a2c.Http.Headers;
            ResponseContent = a2c.Http.ResponseContent;
            StatusCode = a2c.Http.StatusCode;
            // List<Cookie> responseCookies = cookieContainer.GetCookies(baseUri).Cast<Cookie>().ToList();

            if (!isQuiet) {
                _console.WriteLine(ResponseContent, category: "cli.get", code: "response.content");
            }
        }
        catch (HttpRequestException ex) {
            _console.WriteError($"{Constants.ErrorChar} Error: HTTP request failed - {ex.Message}", category: "cli.get", code: "http.request.failed", ex: ex, ctx: new Dictionary<string, object?> { ["baseUrl"] = baseUrl, ["endpoint"] = endpoint });
            return Result.Error;
        }

        return result;
    }
}
