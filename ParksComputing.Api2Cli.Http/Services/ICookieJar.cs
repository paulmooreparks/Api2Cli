using System;
using System.Collections.Generic;
using System.Net.Http;

using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Cli.Http.Services;

public record CookieInfo(string Name, string Value, string Domain, string Path, DateTimeOffset? ExpiresUtc, bool Secure, bool HttpOnly) {
    public bool IsExpired() => ExpiresUtc is not null && ExpiresUtc <= DateTimeOffset.UtcNow;
}

public interface ICookieJar {
    void Capture(HttpResponseMessage response, Uri requestUri);
    string? BuildCookieHeader(Uri requestUri, IEnumerable<string>? existingHeaders);
    IEnumerable<CookieInfo> List();
    void Set(CookieInfo cookie);
    bool Delete(string name, string? domain = null, string? path = null);
    void Clear();
}

internal class CookieJar : ICookieJar {
    private readonly IStoreService _store;
    private const string Prefix = "cookie|"; // cookie|domain|path|name

    public CookieJar(IStoreService store) { _store = store; }

    public void Capture(HttpResponseMessage response, Uri requestUri) {
        if (response.Headers.TryGetValues("Set-Cookie", out var values)) {
            foreach (var set in values) {
                var parsed = ParseSetCookie(set, requestUri.Host);

                if (parsed is null) {
                    continue;
                }

                // Default path fallback
                var cookie = parsed with { Path = string.IsNullOrEmpty(parsed.Path) ? "/" : parsed.Path };

                if (cookie.IsExpired()) {
                    Delete(cookie.Name, cookie.Domain, cookie.Path);
                }
                else {
                    Persist(cookie);
                }
            }
        }
    }

    public string? BuildCookieHeader(Uri requestUri, IEnumerable<string>? existingHeaders) {
        // If user already set Cookie header explicitly, don't override
        if (existingHeaders != null && existingHeaders.Any(h => h.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase))) {
            return null;
        }
        var candidates = List()
            .Where(c => !c.IsExpired())
            .Where(c => DomainMatches(requestUri.Host, c.Domain))
            .Where(c => requestUri.AbsolutePath.StartsWith(c.Path, StringComparison.OrdinalIgnoreCase));
        var grouped = candidates.ToList();

        if (!grouped.Any()) {
            return null;
        }

        return string.Join("; ", grouped.Select(c => c.Name + "=" + c.Value));
    }

    public IEnumerable<CookieInfo> List() {
        foreach (var kv in _store) {
            if (!kv.Key.StartsWith(Prefix, StringComparison.Ordinal)) {
                continue;
            }

            if (kv.Value is string s) {
                var parts = s.Split('\u001F'); // unit separator

                if (parts.Length >= 7) {
                    DateTimeOffset? exp = long.TryParse(parts[4], out var ticks) && ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
                    yield return new CookieInfo(parts[2], parts[3], parts[1], parts[0], exp, parts[5] == "1", parts[6] == "1");
                }
            }
        }
    }

    public void Set(CookieInfo cookie) => Persist(cookie);

    public bool Delete(string name, string? domain = null, string? path = null) {
        var key = FindKey(name, domain, path);

        if (key is null) {
            return false;
        }

        _store.Remove(key);
        return true;
    }

    public void Clear() {
        var keys = _store.Keys.Where(k => k.StartsWith(Prefix, StringComparison.Ordinal)).ToList();

        foreach (var k in keys) {
            _store.Remove(k);
        }
    }

    private void Persist(CookieInfo cookie) {
        var key = BuildKey(cookie.Domain, cookie.Path, cookie.Name);
        var expTicks = cookie.ExpiresUtc?.UtcDateTime.Ticks ?? 0;
        // Store path|name|value|domain ordering preserved for easier evolution
        var serialized = string.Join('\u001F', new[] {
            cookie.Path, cookie.Domain, cookie.Name, cookie.Value, expTicks.ToString(), cookie.Secure ? "1" : "0", cookie.HttpOnly ? "1" : "0"
        });
        _store[key] = serialized;
    }

    private static bool DomainMatches(string host, string cookieDomain) {
        if (string.Equals(host, cookieDomain, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return host.EndsWith("." + cookieDomain, StringComparison.OrdinalIgnoreCase);
    }

    private string? FindKey(string name, string? domain, string? path) {
        return _store.Keys.FirstOrDefault(k => k.StartsWith(Prefix, StringComparison.Ordinal) && k.EndsWith("|" + name, StringComparison.Ordinal) &&
            (domain == null || k.Contains("|" + domain + "|", StringComparison.Ordinal)) && (path == null || k.Contains(Prefix + domain + "|" + path + "|")));
    }

    private static string BuildKey(string domain, string path, string name) => Prefix + domain + "|" + path + "|" + name;

    private static CookieInfo? ParseSetCookie(string header, string fallbackDomain) {
        // Split by ';'
        var segments = header.Split(';');

        if (segments.Length == 0) {
            return null;
        }

        var nameValue = segments[0].Split('=', 2);

        if (nameValue.Length != 2) {
            return null;
        }

        string name = nameValue[0].Trim();
        string value = nameValue[1].Trim();
        string domain = fallbackDomain;
        string path = "/";
        DateTimeOffset? expires = null;
        bool secure = false; bool httpOnly = false;

        foreach (var seg in segments.Skip(1)) {
            var part = seg.Trim();
            if (part.Equals("secure", StringComparison.OrdinalIgnoreCase)) {
                secure = true;
            }
            else if (part.Equals("httponly", StringComparison.OrdinalIgnoreCase)) {
                httpOnly = true;
            }
            else if (part.StartsWith("domain=", StringComparison.OrdinalIgnoreCase)) {
                domain = part[7..].Trim('.');
            }
            else if (part.StartsWith("path=", StringComparison.OrdinalIgnoreCase)) {
                path = part[5..];
            }
            else if (part.StartsWith("expires=", StringComparison.OrdinalIgnoreCase)) {
                if (DateTimeOffset.TryParse(part[8..], out var dt)) {
                    expires = dt.ToUniversalTime();
                }
            }
            else if (part.StartsWith("max-age=", StringComparison.OrdinalIgnoreCase)) {
                if (int.TryParse(part[8..], out var secs)) {
                    expires = DateTimeOffset.UtcNow.AddSeconds(secs);
                }
            }
        }

        return new CookieInfo(name, value, domain, path, expires, secure, httpOnly);
    }
}
