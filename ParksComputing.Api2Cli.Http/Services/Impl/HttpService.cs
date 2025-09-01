using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ParksComputing.Api2Cli.Diagnostics.Services;

namespace ParksComputing.Api2Cli.Http.Services.Impl;

public class HttpService : IHttpService {
    private readonly HttpClient _httpClient;
    private readonly IAppDiagnostics<IHttpService> _appDiagnostics;
    private readonly ICookieJar _cookieJar;

    public HttpService(HttpClient httpClient, IAppDiagnostics<IHttpService> appDiagnostics, ICookieJar cookieJar) {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _appDiagnostics = appDiagnostics ?? throw new ArgumentNullException(nameof(appDiagnostics));
        _cookieJar = cookieJar ?? throw new ArgumentNullException(nameof(cookieJar));
    }

    private static void AddHeaders(HttpRequestMessage request, IEnumerable<string>? headers) {
        if (headers is not null) {
            foreach (var header in headers) {
                var parts = header.Split(new[] { ':' }, 2);

                if (parts.Length == 2) {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    if (!request.Headers.TryAddWithoutValidation(key, value)) {
                        if (request.Content != null && key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) {
                            request.Content.Headers.TryAddWithoutValidation(key, value);
                        }
                    }
                }
            }
        }
    }

    public HttpResponseMessage Get(string baseUrl, IEnumerable<string>? queryParameters, IEnumerable<string>? headers) {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || string.IsNullOrWhiteSpace(baseUri.Scheme)) {
            throw new HttpRequestException($"Error: Invalid base URL: {baseUrl}");
        }

        var uriBuilder = new UriBuilder(baseUri);
        var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);

        if (queryParameters is not null) {
            foreach (var param in queryParameters) {
                if (param.Contains("=")) {
                    var parts = param.Split(['='], 2);
                    query[parts[0]] = parts.Length == 2 ? parts[1] : "";
                }
                else {
                    query[param] = "";
                }
            }
        }

        uriBuilder.Query = query.ToString();
        var finalUrl = uriBuilder.ToString();

        using var request = new HttpRequestMessage(HttpMethod.Get, finalUrl);
        AddHeaders(request, headers);
        InjectCookies(request, headers);
        var response = _httpClient.Send(request);
        _cookieJar.Capture(response, request.RequestUri!);
        return response;
    }

    public async Task<HttpResponseMessage> GetAsync(string baseUrl, IEnumerable<string>? queryParameters, IEnumerable<string>? headers) {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || string.IsNullOrWhiteSpace(baseUri.Scheme)) {
            throw new HttpRequestException($"Error: Invalid base URL: {baseUrl}");
        }

        var uriBuilder = new UriBuilder(baseUri);
        var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);

        if (queryParameters is not null) {
            foreach (var param in queryParameters) {
                if (param.Contains("=")) {
                    var parts = param.Split(['='], 2);
                    query[parts[0]] = parts.Length == 2 ? parts[1] : "";
                }
                else {
                    query[param] = "";
                }
            }
        }

        uriBuilder.Query = query.ToString();
        var finalUrl = uriBuilder.ToString();

        using var request = new HttpRequestMessage(HttpMethod.Get, finalUrl);
        AddHeaders(request, headers);
        InjectCookies(request, headers);
        var response = await _httpClient.SendAsync(request);
        _cookieJar.Capture(response, request.RequestUri!);
        return response;
    }

    public HttpResponseMessage Post(string baseUrl, string? payload, IEnumerable<string>? headers) {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || string.IsNullOrWhiteSpace(baseUri.Scheme)) {
            throw new HttpRequestException($"Error: Invalid base URL: {baseUrl}");
        }

        string? contentType = headers?
            .FirstOrDefault(h => h.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
            ?.Split(':', 2)[1]
            .Trim();

        if (string.IsNullOrEmpty(contentType) && !string.IsNullOrEmpty(payload)) {
            contentType = "application/octet-stream";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUri) {
            Content = new StringContent(payload ?? "", Encoding.UTF8, contentType ?? "text/plain")
        };

        AddHeaders(request, headers);
        InjectCookies(request, headers);
        var response = _httpClient.Send(request);
        _cookieJar.Capture(response, request.RequestUri!);
        return response;
    }

    public async Task<HttpResponseMessage> PostAsync(string baseUrl, string? payload, IEnumerable<string>? headers) {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || string.IsNullOrWhiteSpace(baseUri.Scheme)) {
            throw new HttpRequestException($"Error: Invalid base URL: {baseUrl}");
        }

        string? contentType = headers?
            .FirstOrDefault(h => h.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
            ?.Split(':', 2)[1]
            .Trim();

        if (string.IsNullOrEmpty(contentType) && !string.IsNullOrEmpty(payload)) {
            contentType = "application/octet-stream";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUri) {
            Content = new StringContent(payload ?? "", Encoding.UTF8, contentType ?? "text/plain")
        };

        AddHeaders(request, headers);
        InjectCookies(request, headers);
        var response = await _httpClient.SendAsync(request);
        _cookieJar.Capture(response, request.RequestUri!);
        return response;
    }

    public HttpResponseMessage Put(string baseUrl, string? payload, IEnumerable<string>? headers) {
        throw new NotImplementedException();
    }

    public async Task<HttpResponseMessage> PutAsync(string baseUrl, string? payload, IEnumerable<string>? headers) {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || string.IsNullOrWhiteSpace(baseUri.Scheme)) {
            throw new HttpRequestException($"Error: Invalid base URL: {baseUrl}");
        }

        string? contentType = headers?
            .FirstOrDefault(h => h.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
            ?.Split(':', 2)[1]
            .Trim();

        if (string.IsNullOrEmpty(contentType) && !string.IsNullOrEmpty(payload)) {
            contentType = "application/octet-stream";
        }

        using var request = new HttpRequestMessage(HttpMethod.Put, baseUri) {
            Content = new StringContent(payload ?? "", Encoding.UTF8, contentType ?? "text/plain")
        };

        AddHeaders(request, headers);
        InjectCookies(request, headers);
        var response = await _httpClient.SendAsync(request);
        _cookieJar.Capture(response, request.RequestUri!);
        return response;
    }

    public HttpResponseMessage Patch(string baseUrl, string? payload, IEnumerable<string>? headers) {
        throw new NotImplementedException();
    }

    public async Task<HttpResponseMessage> PatchAsync(string baseUrl, string? payload, IEnumerable<string>? headers) {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || string.IsNullOrWhiteSpace(baseUri.Scheme)) {
            throw new HttpRequestException($"Error: Invalid base URL: {baseUrl}");
        }

        string? contentType = headers?
            .FirstOrDefault(h => h.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
            ?.Split(':', 2)[1]
            .Trim();

        if (string.IsNullOrEmpty(contentType) && !string.IsNullOrEmpty(payload)) {
            contentType = "application/octet-stream";
        }

        using var request = new HttpRequestMessage(HttpMethod.Patch, baseUri) {
            Content = new StringContent(payload ?? "", Encoding.UTF8, contentType ?? "text/plain")
        };

        AddHeaders(request, headers);
        InjectCookies(request, headers);
        var response = await _httpClient.SendAsync(request);
        _cookieJar.Capture(response, request.RequestUri!);
        return response;
    }

    public async Task<HttpResponseMessage> DeleteAsync(string baseUrl, IEnumerable<string>? headers) {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || string.IsNullOrWhiteSpace(baseUri.Scheme)) {
            throw new HttpRequestException($"Error: Invalid base URL: {baseUrl}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, baseUri);
        AddHeaders(request, headers);
        InjectCookies(request, headers);
        var response = await _httpClient.SendAsync(request);
        _cookieJar.Capture(response, request.RequestUri!);
        return response;
    }

    public HttpResponseMessage Delete(string baseUrl, IEnumerable<string>? headers) {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || string.IsNullOrWhiteSpace(baseUri.Scheme)) {
            throw new HttpRequestException($"Error: Invalid base URL: {baseUrl}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, baseUri);
        AddHeaders(request, headers);
        InjectCookies(request, headers);
        var response = _httpClient.Send(request);
        _cookieJar.Capture(response, request.RequestUri!);
        return response;
    }

    public HttpResponseMessage Head(string baseUrl, IEnumerable<string>? headers) {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || string.IsNullOrWhiteSpace(baseUri.Scheme)) {
            throw new HttpRequestException($"Error: Invalid base URL: {baseUrl}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Head, baseUri);
        AddHeaders(request, headers);
        InjectCookies(request, headers);
        var response = _httpClient.Send(request);
        _cookieJar.Capture(response, request.RequestUri!);
        return response;
    }

    public async Task<HttpResponseMessage?> HeadAsync(string baseUrl, IEnumerable<string>? headers) {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || string.IsNullOrWhiteSpace(baseUri.Scheme)) {
            throw new HttpRequestException($"Error: Invalid base URL: {baseUrl}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Head, baseUri);
        AddHeaders(request, headers);
        InjectCookies(request, headers);
        var response = await _httpClient.SendAsync(request);
        _cookieJar.Capture(response, request.RequestUri!);
        return response;
    }

    public HttpResponseMessage Options(string baseUrl, IEnumerable<string>? headers) {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || string.IsNullOrWhiteSpace(baseUri.Scheme)) {
            throw new HttpRequestException($"Error: Invalid base URL: {baseUrl}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Options, baseUri);
        AddHeaders(request, headers);
        InjectCookies(request, headers);
        var response = _httpClient.Send(request);
        _cookieJar.Capture(response, request.RequestUri!);
        return response;
    }

    public async Task<HttpResponseMessage?> OptionsAsync(string baseUrl, IEnumerable<string>? headers) {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || string.IsNullOrWhiteSpace(baseUri.Scheme)) {
            throw new HttpRequestException($"Error: Invalid base URL: {baseUrl}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Options, baseUri);
        AddHeaders(request, headers);
        InjectCookies(request, headers);
        var response = await _httpClient.SendAsync(request);
        _cookieJar.Capture(response, request.RequestUri!);
        return response;
    }

    private void InjectCookies(HttpRequestMessage request, IEnumerable<string>? existingHeaders) {
        try {
            if (request.RequestUri is null) {
                return;
            }

            var header = _cookieJar.BuildCookieHeader(request.RequestUri, existingHeaders);

            if (!string.IsNullOrEmpty(header)) {
                request.Headers.TryAddWithoutValidation("Cookie", header);
            }
        }
        catch (Exception ex) {
            // Cookie injection is intentionally non-fatal; emit diagnostic for visibility without failing request.
            _appDiagnostics.Emit("CookieInjectionError", new { Error = ex.Message });
        }
    }
}
