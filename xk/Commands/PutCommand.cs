using System.Collections.Generic;
using System.Net.Http.Headers;

using Cliffer;

using ParksComputing.XferKit.Api;
using ParksComputing.XferKit.Workspace;

namespace ParksComputing.XferKit.Cli.Commands;

[Command("put", "Send resources to the specified API endpoint via a PUT request.")]
[Argument(typeof(string), "endpoint", "The endpoint to send the PUT request to.")]
[Option(typeof(string), "--baseurl", "The base URL of the API.", new[] { "-b" }, IsRequired = false)]
[Option(typeof(IEnumerable<string>), "--headers", "Headers to include in the request.", new[] { "-h" }, AllowMultipleArgumentsPerToken = true, Arity = ArgumentArity.ZeroOrMore)]
[Option(typeof(string), "--payload", "Content to send with the request. If input is redirected, content can also be read from standard input.", new[] { "-p" }, Arity = ArgumentArity.ZeroOrOne)]
internal class PutCommand {
    private readonly XferKitApi _xk;

    public string ResponseContent { get; protected set; } = string.Empty;
    public int StatusCode { get; protected set; } = 0;
    public HttpResponseHeaders? Headers { get; protected set; }

    public PutCommand(
        XferKitApi xk
        ) 
    {
        _xk = xk;
    }

    public int Execute(
        [ArgumentParam("endpoint")] string endpoint,
        [OptionParam("--baseurl")] string? baseUrl,
        [OptionParam("--headers")] IEnumerable<string>? headers,
        [OptionParam("--payload")] string? payload
    ) 
    {
        // Validate URL format
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var baseUri) || string.IsNullOrWhiteSpace(baseUri.Scheme)) {
            baseUrl ??= _xk.activeWorkspace.BaseUrl;

            if (string.IsNullOrEmpty(baseUrl) || !Uri.TryCreate(new Uri(baseUrl), endpoint, out baseUri) || string.IsNullOrWhiteSpace(baseUri.Scheme)) {
                Console.Error.WriteLine($"{Constants.ErrorChar} Error: Invalid base URL: {baseUrl}");
                return Result.ErrorInvalidArgument;
            }
        }

        baseUrl = baseUri.ToString();
        var paramList = new List<string>();

        if (string.IsNullOrEmpty(payload) && Console.IsInputRedirected) {
            var payloadString = Console.In.ReadToEnd();
            payload = payloadString.Trim();
        }

        int result = Result.Success;

        try { 
            var response = _xk.http.put(baseUrl ?? "", endpoint, payload, headers);

            if (response != null) {
                Headers = response.Headers;
                using var reader = new System.IO.StreamReader(response.Content.ReadAsStream());
                ResponseContent = reader.ReadToEnd();
                StatusCode = (int)response.StatusCode;
            }

            if (response is null) {
                Console.Error.WriteLine($"{Constants.ErrorChar} Error: No response received from {baseUrl}");
                result = Result.Error;
            }
            else if (!response.IsSuccessStatusCode) {
                Console.Error.WriteLine($"{Constants.ErrorChar} Error: {response.StatusCode} - {ResponseContent}");
                result = Result.Error;
            }
            else {
                if (!string.IsNullOrEmpty(ResponseContent)) {
                    Console.WriteLine(ResponseContent);
                }
            }
        }
        catch (Exception ex) {
            Console.Error.WriteLine($"{Constants.ErrorChar} Error: {ex.Message}");
            result = Result.Error;
        }

        return result;
    }
}
