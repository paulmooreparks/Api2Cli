using System.Collections.Generic;
using System.Net.Http.Headers;

using Cliffer;

using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Workspace;

namespace ParksComputing.Api2Cli.Cli.Commands;

[Command("put", "Send resources to the specified API endpoint via a PUT request.")]
[Argument(typeof(string), "payload", "Content to send with the request. If input is redirected, content can also be read from standard input.")]
[Option(typeof(string), "--endpoint", "The endpoint to send the PUT request to.", new[] { "-e" }, IsRequired = false, Arity = ArgumentArity.ExactlyOne)]
[Option(typeof(string), "--baseurl", "The base URL of the API.", new[] { "-b" }, IsRequired = false)]
[Option(typeof(IEnumerable<string>), "--headers", "Headers to include in the request.", new[] { "-h" }, AllowMultipleArgumentsPerToken = true, Arity = ArgumentArity.ZeroOrMore)]
internal class PutCommand(
    A2CApi a2c
    )
{
    public string ResponseContent { get; protected set; } = string.Empty;
    public int StatusCode { get; protected set; } = 0;
    public HttpResponseHeaders? Headers { get; protected set; }

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
                Console.Error.WriteLine($"{Constants.ErrorChar} Error: Invalid base URL: {baseUrl}");
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
                Console.Error.WriteLine($"{Constants.ErrorChar} Error: No response received from {baseUrl}");
                result = Result.Error;
            }
            else if (!response.IsSuccessStatusCode) {
                Console.Error.WriteLine($"{Constants.ErrorChar} {(int)response.StatusCode} {response.ReasonPhrase} at {baseUrl}");
                result = Result.Error;
            }

            Headers = a2c.Http.Headers;
            ResponseContent = a2c.Http.ResponseContent;
            StatusCode = a2c.Http.StatusCode;
        }
        catch (Exception ex) {
            Console.Error.WriteLine($"{Constants.ErrorChar} Error: {ex.Message}");
            result = Result.Error;
        }

        return result;
    }
}
