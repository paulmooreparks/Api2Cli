using System;
using System.Collections.Generic;
using System.Linq;
using ParksComputing.Api2Cli.Api.Cookies;
using ParksComputing.Api2Cli.Http.Services;

namespace ParksComputing.Api2Cli.Api.Cookies.Impl;

internal class CookieApi(ICookieJar jar) : ICookieApi {
    public IEnumerable<CookieDto> list(string? domain = null, string? path = null, bool includeExpired = false) {
        var q = jar.List();

        if (domain != null)
        {
            var dn = NormalizeDomain(domain);
            q = q.Where(c => string.Equals(c.Domain, dn, StringComparison.OrdinalIgnoreCase));
        }

        if (path != null)
        {
            var pn = NormalizePath(path);
            q = q.Where(c => string.Equals(c.Path, pn, StringComparison.OrdinalIgnoreCase));
        }

        if (!includeExpired)
        {
            q = q.Where(c => !c.IsExpired());
        }

        return q.Select(c => new CookieDto(c.Name, c.Value, c.Domain, c.Path, c.ExpiresUtc, c.Secure, c.HttpOnly)).ToArray();
    }

    public void set(CookieSetRequest request) {
        if (request is null) {
            throw new ArgumentNullException(nameof(request));
        }

        var name = request.Name?.Trim();

        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("name required");
        }

        var domain = NormalizeDomain(request.Domain);
        var path = NormalizePath(request.Path ?? "/");
        DateTimeOffset? expires = request.ExpiresUtc;

        if (request.TtlSeconds is int ttl && ttl > 0) {
            expires = DateTimeOffset.UtcNow.AddSeconds(ttl);
        }

        var cookie = new CookieInfo(name!, request.Value ?? string.Empty, domain, path, expires, request.Secure, request.HttpOnly);

        if (cookie.IsExpired()) {
            jar.Delete(cookie.Name, cookie.Domain, cookie.Path);
            return;
        }

        jar.Set(cookie);
    }

    public bool delete(string name, string? domain = null, string? path = null) {
        if (string.IsNullOrWhiteSpace(name)) {
            return false;
        }

        return jar.Delete(name, domain != null ? NormalizeDomain(domain) : null, path != null ? NormalizePath(path) : null);
    }

    public int clear() {
        var count = jar.List().Count();
        jar.Clear();
        return count;
    }

    public string? buildHeader(string url) {
        if (string.IsNullOrWhiteSpace(url)) {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
            return null;
        }

        return jar.BuildCookieHeader(uri, null);
    }

    private static string NormalizeDomain(string d) => d.Trim().TrimStart('.');

    private static string NormalizePath(string p) {
        if (string.IsNullOrWhiteSpace(p)) {
            return "/";
        }

        return p.StartsWith('/') ? p : "/" + p;
    }
}
