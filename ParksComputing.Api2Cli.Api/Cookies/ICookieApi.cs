using System;
using System.Collections.Generic;

namespace ParksComputing.Api2Cli.Api.Cookies;

public interface ICookieApi {
    IEnumerable<CookieDto> list(string? domain = null, string? path = null, bool includeExpired = false);
    void set(CookieSetRequest request);
    bool delete(string name, string? domain = null, string? path = null);
    int clear();
    string? buildHeader(string url);
}

public record CookieDto(string Name, string Value, string Domain, string Path, DateTimeOffset? ExpiresUtc, bool Secure, bool HttpOnly);
public record CookieSetRequest(string Name, string Value, string Domain, string? Path = "/", DateTimeOffset? ExpiresUtc = null, int? TtlSeconds = null, bool Secure = false, bool HttpOnly = false);
