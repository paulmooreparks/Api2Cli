using System;
using Cliffer;
using ParksComputing.Api2Cli.Http.Services;
using ParksComputing.Api2Cli.Cli.Services;

namespace ParksComputing.Api2Cli.Cli.Commands.StoreCommand.SubCommands;

[Command("set", "Add or update a cookie", Parent = "cookies")]
[Argument(typeof(string), "domain", "Cookie domain (e.g. example.com)")]
[Argument(typeof(string), "path", "Cookie path (e.g. / or /app)")]
[Argument(typeof(string), "name", "Cookie name")]
[Argument(typeof(string), "value", "Cookie value")]
[Option(typeof(string), "--expires", "Absolute (UTC/RFC1123/ISO) or relative +3600,+5m,+2h,+7d", IsRequired = false)]
[Option(typeof(bool), "--secure", "Mark cookie Secure", IsRequired = false)]
[Option(typeof(bool), "--http-only", "Mark cookie HttpOnly", IsRequired = false)]
internal class CookiesSetCommand(ICookieJar jar, IConsoleWriter console) {
    public int Execute(
        [ArgumentParam("domain")] string domain,
        [ArgumentParam("path")] string path,
        [ArgumentParam("name")] string name,
        [ArgumentParam("value")] string value,
        [OptionParam("--expires")] string? expires = null,
        [OptionParam("--secure")] bool secure = false,
        [OptionParam("--http-only")] bool httpOnly = false
        ) {
        if (string.IsNullOrWhiteSpace(name)) {
            console.WriteError("Name required", category: "cli.cookies.set", code: "cookies.set.nameRequired");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(domain)) {
            console.WriteError("Domain required", category: "cli.cookies.set", code: "cookies.set.domainRequired");
            return 1;
        }

        domain = NormalizeDomain(domain);
        path = NormalizePath(path);

        DateTimeOffset? expiresUtc = null;

        if (!string.IsNullOrWhiteSpace(expires)) {
            if (!TryParseExpires(expires!, out expiresUtc)) {
                console.WriteError($"Invalid --expires '{expires}'. Use ISO/RFC1123 or +secs/+5m/+2h/+7d", category: "cli.cookies.set", code: "cookies.set.invalidExpires");
                return 2;
            }

            if (expiresUtc != null && expiresUtc <= DateTimeOffset.UtcNow) {
                console.WriteError("Expiry is in the past; treating as delete.", category: "cli.cookies.set", code: "cookies.set.expired");
            }
        }

        var info = new CookieInfo(name, value, domain, path, expiresUtc, secure, httpOnly);

        if (expiresUtc != null && expiresUtc <= DateTimeOffset.UtcNow) {
            jar.Delete(name, domain, path);
            console.WriteLine($"Deleted expired cookie {domain}\t{path}\t{name}", category: "cli.cookies.set", code: "cookies.set.deletedExpired");
            return Result.Success;
        }

        jar.Set(info);

    console.WriteLine($"Set cookie {domain}\t{path}\t{name}={value} (exp={(expiresUtc?.ToString("u") ?? "session")})", category: "cli.cookies.set", code: "cookies.set.ok");
        return Result.Success;
    }

    private static string NormalizeDomain(string d) => d.Trim().TrimStart('.');

    private static string NormalizePath(string p) {
        if (string.IsNullOrWhiteSpace(p)) {
            return "/";
        }

        if (!p.StartsWith('/')) {
            p = "/" + p;
        }

        return p;
    }

    private static bool TryParseExpires(string raw, out DateTimeOffset? expires) {
        expires = null;
        raw = raw.Trim();

        if (raw.StartsWith('+')) {
            var body = raw[1..];

            if (body.Length == 0) {
                return false;
            }

            char last = body[^1];
            double factor = 1; string numberPortion = body;

            switch (char.ToLowerInvariant(last)) {
                case 's': factor = 1; numberPortion = body[..^1]; break;
                case 'm': factor = 60; numberPortion = body[..^1]; break;
                case 'h': factor = 3600; numberPortion = body[..^1]; break;
                case 'd': factor = 86400; numberPortion = body[..^1]; break;
                case 'w': factor = 604800; numberPortion = body[..^1]; break;
                default: if (!char.IsDigit(last)) { return false; } break;
            }

            if (!double.TryParse(numberPortion, out var units)) {
                return false;
            }

            expires = DateTimeOffset.UtcNow.AddSeconds(units * factor);
            return true;
        }

        if (DateTimeOffset.TryParse(raw, out var abs)) {
            expires = abs.ToUniversalTime();
            return true;
        }

        return false;
    }
}
