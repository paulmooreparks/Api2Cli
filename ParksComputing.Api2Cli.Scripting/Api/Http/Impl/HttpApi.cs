using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ParksComputing.Api2Cli.Http.Services;
using System.Net;

namespace ParksComputing.Api2Cli.Api.Http.Impl;

internal class HttpApi : IHttpApi
{
    private readonly IHttpService _httpService;

    public string ResponseContent { get; protected set; } = string.Empty;
    public int StatusCode { get; protected set; } = 0;
    public System.Net.Http.Headers.HttpResponseHeaders? Headers { get; protected set; } = default;

    public HttpApi(
        IHttpService httpService
        ) 
    {
        _httpService = httpService;
    }

    public HttpResponseMessage? Get(
        string baseUrl,
        IEnumerable<string>? queryParameters,
        IEnumerable<string>? headers
        ) {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler() {
            CookieContainer = cookieContainer,
            UseCookies = true
        };

        var response = _httpService.Get(
            baseUrl,
            queryParameters,
            headers
            );

        if (response != null) {
            this.Headers = response.Headers;
            using (var stream = response.Content.ReadAsStream())
            using (var reader = new StreamReader(stream)) {
                ResponseContent = reader.ReadToEnd();
            }
            StatusCode = (int)response.StatusCode;
            // List<Cookie> responseCookies = cookieContainer.GetCookies(baseUri).Cast<Cookie>().ToList();
        }

        return response;
    }

    public async Task<HttpResponseMessage?> GetAsync(
        string baseUrl,
        IEnumerable<string>? queryParameters,
        IEnumerable<string>? headers
        )
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler() {
            CookieContainer = cookieContainer,
            UseCookies = true
        };

        var response = await _httpService.GetAsync(
            baseUrl,
            queryParameters,
            headers
            );

        if (response != null) {
            this.Headers = response.Headers;
            ResponseContent = await response.Content.ReadAsStringAsync();
            StatusCode = (int)response.StatusCode;
            // List<Cookie> responseCookies = cookieContainer.GetCookies(baseUri).Cast<Cookie>().ToList();
        }

        return response;
    }

    public HttpResponseMessage? Post(
        string baseUrl,
        string? payload,
        IEnumerable<string>? headers
        )
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler() {
            CookieContainer = cookieContainer,
            UseCookies = true
        };

        var response = _httpService.Post(
            baseUrl,
            payload,
            headers
            );

        if (response != null) {
            this.Headers = response.Headers;
            using (var stream = response.Content.ReadAsStream())
            using (var reader = new StreamReader(stream)) {
                ResponseContent = reader.ReadToEnd();
            }
            StatusCode = (int)response.StatusCode;
            // List<Cookie> responseCookies = cookieContainer.GetCookies(baseUri).Cast<Cookie>().ToList();
        }

        return response;
    }

    public async Task<HttpResponseMessage?> PostAsync(
        string baseUrl,
        string? payload,
        IEnumerable<string>? headers
        )
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler() {
            CookieContainer = cookieContainer,
            UseCookies = true
        };

        var response = await _httpService.PostAsync(
            baseUrl,
            payload,
            headers
            );

        if (response != null) {
            this.Headers = response.Headers;
            ResponseContent = await response.Content.ReadAsStringAsync();
            StatusCode = (int)response.StatusCode;
            // List<Cookie> responseCookies = cookieContainer.GetCookies(baseUri).Cast<Cookie>().ToList();
        }

        return response;
    }

    public async Task<HttpResponseMessage?> PutAsync(
        string baseUrl,
        string? payload,
        IEnumerable<string>? headers
        )
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler() {
            CookieContainer = cookieContainer,
            UseCookies = true
        };

        var response = await _httpService.PutAsync(
            baseUrl,
            payload,
            headers
        );

        if (response != null) {
            this.Headers = response.Headers;
            ResponseContent = await response.Content.ReadAsStringAsync();
            StatusCode = (int)response.StatusCode;
            // List<Cookie> responseCookies = cookieContainer.GetCookies(baseUri).Cast<Cookie>().ToList();
        }

        return response;
    }

    public HttpResponseMessage? Put(
        string baseUrl,
        string? payload,
        IEnumerable<string>? headers
        )
    {
        return PutAsync(baseUrl, payload, headers).GetAwaiter().GetResult();
    }

    public async Task<HttpResponseMessage?> PatchAsync(string baseUrl, string? payload, IEnumerable<string>? headers) {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler() {
            CookieContainer = cookieContainer,
            UseCookies = true
        };

        var response = await _httpService.PatchAsync(
            baseUrl,
            payload,
            headers
        );

        if (response != null) {
            this.Headers = response.Headers;
            ResponseContent = await response.Content.ReadAsStringAsync();
            StatusCode = (int)response.StatusCode;
            // List<Cookie> responseCookies = cookieContainer.GetCookies(baseUri).Cast<Cookie>().ToList();
        }

        return response;
    }

    public HttpResponseMessage? Patch(string baseUrl, string? payload, IEnumerable<string>? headers) {
        return PatchAsync(baseUrl, payload, headers).GetAwaiter().GetResult();
    }

    public async Task<HttpResponseMessage?> DeleteAsync(
        string baseUrl,
        IEnumerable<string>? headers
        )
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler() {
            CookieContainer = cookieContainer,
            UseCookies = true
        };

        return await _httpService.DeleteAsync(
            baseUrl,
            headers
        );
    }

    public HttpResponseMessage? Delete(
        string baseUrl,
        IEnumerable<string>? headers
        )
    {
        return DeleteAsync(baseUrl, headers).GetAwaiter().GetResult();
    }

    public async Task<HttpResponseMessage?> HeadAsync(
        string baseUrl,
        IEnumerable<string>? headers
    ) {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler() {
            CookieContainer = cookieContainer,
            UseCookies = true
        };

        var response = await _httpService.HeadAsync(
            baseUrl,
            headers
        );

        if (response != null) {
            this.Headers = response.Headers;
            // HEAD responses do not have a body
            ResponseContent = string.Empty;
            StatusCode = (int)response.StatusCode;
        }

        return response;
    }

    public HttpResponseMessage? Head(
        string baseUrl,
        IEnumerable<string>? headers
    ) {
        return HeadAsync(baseUrl, headers).GetAwaiter().GetResult();
    }

    public async Task<HttpResponseMessage?> OptionsAsync(
        string baseUrl,
        IEnumerable<string>? headers
    ) {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler() {
            CookieContainer = cookieContainer,
            UseCookies = true
        };

        var response = await _httpService.OptionsAsync(
            baseUrl,
            headers
        );

        if (response != null) {
            this.Headers = response.Headers;
            ResponseContent = await response.Content.ReadAsStringAsync();
            StatusCode = (int)response.StatusCode;
        }

        return response;
    }

    public HttpResponseMessage? Options(
        string baseUrl,
        IEnumerable<string>? headers
    ) {
        return OptionsAsync(baseUrl, headers).GetAwaiter().GetResult();
    }
}
