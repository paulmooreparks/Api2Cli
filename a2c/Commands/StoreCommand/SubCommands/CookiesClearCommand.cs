using System;
using Cliffer;
using ParksComputing.Api2Cli.Http.Services;
using ParksComputing.Api2Cli.Cli.Services;

namespace ParksComputing.Api2Cli.Cli.Commands.StoreCommand.SubCommands;

[Command("clear", "Clear all cookies for current workspace", Parent = "cookies")]
[Option(typeof(bool), "--force", "Skip confirmation", IsRequired = false)]
internal class CookiesClearCommand(ICookieJar jar, IConsoleWriter console) {
    private readonly ICookieJar _jar = jar;
    public int Execute([OptionParam("--force")] bool force = false) {
        if (!force) {
            console.Write("This will delete ALL cookies for this workspace. Continue? [y/N] ", category: "cli.cookies.clear", code: "cookies.clear.confirmPrompt");
            var key = Console.ReadLine(); // reading still via Console
            if (!string.Equals(key, "y", StringComparison.OrdinalIgnoreCase) && key != "yes") {
                console.WriteLine("Aborted", category: "cli.cookies.clear", code: "cookies.clear.aborted");
                return Result.Success;
            }
        }
        _jar.Clear();
        console.WriteLine("Cleared all cookies.", category: "cli.cookies.clear", code: "cookies.clear.ok");
        return Result.Success;
    }
}
