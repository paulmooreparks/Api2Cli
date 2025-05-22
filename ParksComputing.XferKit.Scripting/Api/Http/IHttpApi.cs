using Microsoft.ClearScript;

using System.Net.Http.Headers;

namespace ParksComputing.XferKit.Api.Http;

public interface IHttpApi {
    [ScriptMember("headers")]
    HttpResponseHeaders? Headers { get; }
    [ScriptMember("responseContent")]
    string ResponseContent { get; }
    [ScriptMember("statusCode")]
    int StatusCode { get; }

    [ScriptMember("get")]
    HttpResponseMessage? Get(string baseUrl, IEnumerable<string>? queryParameters, IEnumerable<string>? headers);
    [ScriptMember("getAsync")]
    Task<HttpResponseMessage?> GetAsync(string baseUrl, IEnumerable<string>? queryParameters, IEnumerable<string>? headers);
    [ScriptMember("post")]
    HttpResponseMessage? Post(string baseUrl, string? payload, IEnumerable<string>? headers);
    [ScriptMember("postAsync")]
    Task<HttpResponseMessage?> PostAsync(string baseUrl, string? payload, IEnumerable<string>? headers);
    [ScriptMember("put")]
    HttpResponseMessage? Put(string baseUrl, string? payload, IEnumerable<string>? headers);
    [ScriptMember("putAsync")]
    Task<HttpResponseMessage?> PutAsync(string baseUrl, string? payload, IEnumerable<string>? headers);
    [ScriptMember("patch")]
    HttpResponseMessage? Patch(string baseUrl, string? payload, IEnumerable<string>? headers);
    [ScriptMember("patchAsync")]
    Task<HttpResponseMessage?> PatchAsync(string baseUrl, string? payload, IEnumerable<string>? headers);
    [ScriptMember("delete")]
    HttpResponseMessage? Delete(string baseUrl, IEnumerable<string>? headers);
    [ScriptMember("deleteAsync")]
    Task<HttpResponseMessage?> DeleteAsync(string baseUrl, IEnumerable<string>? headers);
    [ScriptMember("head")]
    HttpResponseMessage? Head(string baseUrl, IEnumerable<string>? headers = null);
    [ScriptMember("headAsync")]
    Task<HttpResponseMessage?> HeadAsync(string baseUrl, IEnumerable<string>? headers = null);
    [ScriptMember("options")]
    HttpResponseMessage? Options(string baseUrl, IEnumerable<string>? headers = null);
    [ScriptMember("optionsAsync")]
    Task<HttpResponseMessage?> OptionsAsync(string baseUrl, IEnumerable<string>? headers = null);
}