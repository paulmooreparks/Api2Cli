using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParksComputing.Api2Cli.Cli.Utilities;

public static class ConsolePrompts {
    public static bool Confirm(string message, bool defaultValue = false) {
        var defaultHint = defaultValue ? "[Y/n]" : "[y/N]";
        Console.Write($"{message} {defaultHint} ");
        var input = Console.ReadLine()?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(input)){
            return defaultValue;
        }

        return input is "y" or "yes";
    }
}