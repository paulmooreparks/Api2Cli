using System;
using System.Linq;
using Cliffer;
using ParksComputing.Api2Cli.Http.Services;
using ParksComputing.Api2Cli.Cli.Services;

namespace ParksComputing.Api2Cli.Cli.Commands.StoreCommand.SubCommands;

[Command("delete", "Delete cookie(s)", Parent = "cookies")]
[Argument(typeof(string), "name", "Cookie name to delete")]
[Option(typeof(string), "--domain", "Filter by domain", IsRequired = false)]
[Option(typeof(string), "--path", "Filter by path", IsRequired = false)]
[Option(typeof(bool), "--all-matches", "Delete all matching even if ambiguous", IsRequired = false)]
internal class CookiesDeleteCommand(ICookieJar jar, IConsoleWriter console) {
    public int Execute(
        [ArgumentParam("name")] string name,
        [OptionParam("--domain")] string? domain = null,
        [OptionParam("--path")] string? path = null,
        [OptionParam("--all-matches")] bool allMatches = false
        )
    {
        if (string.IsNullOrWhiteSpace(name)) {
            console.WriteError("Name required", category: "cli.cookies.delete", code: "cookies.delete.nameRequired");
            return 1;
        }

        var domainNorm = domain?.Trim().TrimStart('.');
        var pathNorm = NormalizePath(path);

        var matches = jar.List()
            .Where(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
            .Where(c => domainNorm == null || string.Equals(c.Domain, domainNorm, StringComparison.OrdinalIgnoreCase))
            .Where(c => pathNorm == null || string.Equals(c.Path, pathNorm, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0) {
            console.WriteLine("(no matches)", category: "cli.cookies.delete", code: "cookies.delete.none");
            return Result.Success;
        }

        if (matches.Count > 1 && !allMatches && (domainNorm == null || pathNorm == null)) {
            console.WriteError("Ambiguous: multiple matches. Refine with --domain / --path or use --all-matches.", category: "cli.cookies.delete", code: "cookies.delete.ambiguous");

            foreach (var m in matches) {
                console.WriteError($"  {m.Domain}\t{m.Path}\t{m.Name}={m.Value}", category: "cli.cookies.delete", code: "cookies.delete.match");
            }

            return 2;
        }

        int deleted = 0;
        foreach (var m in matches) {
            if (jar.Delete(m.Name, m.Domain, m.Path)) {
                deleted++;
            }
        }

    console.WriteLine($"Deleted {deleted} cookie(s).", category: "cli.cookies.delete", code: "cookies.delete.ok");
        return Result.Success;
    }

    private static string? NormalizePath(string? p) {
        if (string.IsNullOrWhiteSpace(p)) { return p; }
        if (!p.StartsWith('/')) { p = "/" + p; }
        return p;
    }
}
