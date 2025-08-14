using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParksComputing.Api2Cli.Http.Services;

public interface IHttpService {
    HttpResponseMessage Get(string baseUrl, IEnumerable<string>? queryParameters, IEnumerable<string>? headers);
    Task<HttpResponseMessage> GetAsync(string baseUrl, IEnumerable<string>? queryParameters, IEnumerable<string>? headers);
    HttpResponseMessage Post(string baseUrl, string? payload, IEnumerable<string>? headers);
    Task<HttpResponseMessage> PostAsync(string baseUrl, string? payload, IEnumerable<string>? headers);
    HttpResponseMessage Put(string baseUrl, string? payload, IEnumerable<string>? headers);
    Task<HttpResponseMessage> PutAsync(string baseUrl, string? payload, IEnumerable<string>? headers);
    HttpResponseMessage Patch(string baseUrl, string? payload, IEnumerable<string>? headers);
    Task<HttpResponseMessage> PatchAsync(string baseUrl, string? payload, IEnumerable<string>? headers);
    HttpResponseMessage Delete(string baseUrl, IEnumerable<string>? headers);
    Task<HttpResponseMessage> DeleteAsync(string baseUrl, IEnumerable<string>? headers);
    Task<HttpResponseMessage?> HeadAsync(string baseUrl, IEnumerable<string>? headers);
    Task<HttpResponseMessage?> OptionsAsync(string baseUrl, IEnumerable<string>? headers);

}
