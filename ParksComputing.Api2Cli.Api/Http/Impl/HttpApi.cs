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

    public string responseContent { get; protected set; } = string.Empty;
    public int statusCode { get; protected set; } = 0;
    public System.Net.Http.Headers.HttpResponseHeaders? headers { get; protected set; } = default;

    public HttpApi(
        IHttpService httpService
        ) 
    {
        _httpService = httpService;
    }

    public HttpResponseMessage? get(
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
            this.headers = response.Headers;
            using (var stream = response.Content.ReadAsStream())
            using (var reader = new StreamReader(stream)) {
                responseContent = reader.ReadToEnd();
            }
            statusCode = (int)response.StatusCode;
            // List<Cookie> responseCookies = cookieContainer.GetCookies(baseUri).Cast<Cookie>().ToList();
        }

        return response;
    }

    public async Task<HttpResponseMessage?> getAsync(
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
            this.headers = response.Headers;
            responseContent = await response.Content.ReadAsStringAsync();
            statusCode = (int)response.StatusCode;
            // List<Cookie> responseCookies = cookieContainer.GetCookies(baseUri).Cast<Cookie>().ToList();
        }

        return response;
    }

    public HttpResponseMessage? post(
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
            this.headers = response.Headers;
            using (var stream = response.Content.ReadAsStream())
            using (var reader = new StreamReader(stream)) {
                responseContent = reader.ReadToEnd();
            }
            statusCode = (int)response.StatusCode;
            // List<Cookie> responseCookies = cookieContainer.GetCookies(baseUri).Cast<Cookie>().ToList();
        }

        return response;
    }

    public async Task<HttpResponseMessage?> postAsync(
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
            this.headers = response.Headers;
            responseContent = await response.Content.ReadAsStringAsync();
            statusCode = (int)response.StatusCode;
            // List<Cookie> responseCookies = cookieContainer.GetCookies(baseUri).Cast<Cookie>().ToList();
        }

        return response;
    }

    public async Task<HttpResponseMessage?> putAsync(
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
            this.headers = response.Headers;
            responseContent = await response.Content.ReadAsStringAsync();
            statusCode = (int)response.StatusCode;
            // List<Cookie> responseCookies = cookieContainer.GetCookies(baseUri).Cast<Cookie>().ToList();
        }

        return response;
    }

    public HttpResponseMessage? put(
        string baseUrl,
        string? payload,
        IEnumerable<string>? headers
        )
    {
        return putAsync(baseUrl, payload, headers).GetAwaiter().GetResult();
    }

    public async Task<HttpResponseMessage?> patchAsync(string baseUrl, string? payload, IEnumerable<string>? headers) {
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
            this.headers = response.Headers;
            responseContent = await response.Content.ReadAsStringAsync();
            statusCode = (int)response.StatusCode;
            // List<Cookie> responseCookies = cookieContainer.GetCookies(baseUri).Cast<Cookie>().ToList();
        }

        return response;
    }

    public HttpResponseMessage? patch(string baseUrl, string? payload, IEnumerable<string>? headers) {
        return patchAsync(baseUrl, payload, headers).GetAwaiter().GetResult();
    }

    public async Task<HttpResponseMessage?> deleteAsync(
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

    public HttpResponseMessage? delete(
        string baseUrl,
        IEnumerable<string>? headers
        )
    {
        return deleteAsync(baseUrl, headers).GetAwaiter().GetResult();
    }
}
