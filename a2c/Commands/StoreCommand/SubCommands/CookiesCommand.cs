using Cliffer;
using ParksComputing.Api2Cli.Http.Services;
using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Cli.Services;

namespace ParksComputing.Api2Cli.Cli.Commands.StoreCommand.SubCommands;

[Command("cookies", "Manage persisted cookies", Parent = "store")]
internal class CookiesCommand(ICookieJar jar, IConsoleWriter console) {

    // Default action lists cookies
    public int Execute() {
        var list = jar.List().ToList();

        if (list.Count == 0) {
                console.WriteLine("(no cookies)", category: "cli.cookies.list", code: "cookies.none");
            return Result.Success;
        }

        foreach (var c in list.OrderBy(c => c.Domain).ThenBy(c => c.Path).ThenBy(c => c.Name)) {
            var exp = c.ExpiresUtc?.ToString("u") ?? "session";
                console.WriteLine($"{c.Domain}\t{c.Path}\t{c.Name}={c.Value} (exp={exp})", category: "cli.cookies.list", code: "cookies.item");
        }

        return Result.Success;
    }
}
