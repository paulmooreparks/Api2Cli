namespace ParksComputing.XferKit.Scripting.Services;

public class ConsoleScriptObject {
    private static Dictionary<string, int> _counts = new();
    private static int _groupDepth = 0;

    private static string GetIndent() => new string(' ', _groupDepth * 2);

    private static void Write(params object[] args) {
        if (args == null || args.Length == 0) {
            return;
        }

        for (var i = 0; i < args.Length; ++i) {
            Console.Write(args[i]);

            if (i + 1 < args.Length) {
                Console.Write(" ");
            }
        }
    }

    private static void WriteLine(params object[] args) {
        Write(args);
        Console.WriteLine();
    }

    // public static void log(string s) => Console.WriteLine(GetIndent() + s);
    public static void log(params object[] args) {
        Write(GetIndent());
        WriteLine(args);
    }

    public static void info(params object[] args) {
        Write(GetIndent());
        Write("[INFO] ");
        WriteLine(args);
    }

    public static void warn(params object[] args) {
        Write(GetIndent());
        Write("[WARN] ");
        WriteLine(args);
    }

    public static void error(params object[] args) {
        Console.Error.Write(GetIndent());
        Console.Error.Write("[ERROR] ");
        Console.Error.WriteLine(args);
    }

    public static void debug(params object[] args) {
        Write(GetIndent());
        Write("[DEBUG] ");
        WriteLine(args);
    }

    public static void trace(params object[] args) {
        Write(GetIndent());
        Write("[TRACE] ");
        WriteLine(args);
        WriteLine(Environment.StackTrace);
    }

    public static void assert(bool condition, params object[] args) {
        if (!condition) {
            Console.Error.Write(GetIndent());
            Console.Error.Write("[ASSERT] ");
            Console.Error.WriteLine((args.Length > 0 ? args : "Assertion failed"));
        }
    }

    public static void count(string label = "default") {
        if (!_counts.ContainsKey(label)) {
            _counts[label] = 0;
        }
        _counts[label]++;
        Write(GetIndent());
        WriteLine($"{label}: {_counts[label]}");
    }

    public static void countReset(string label = "default") {
        if (_counts.ContainsKey(label)) {
            _counts[label] = 0;
        }
    }

    public static void group(string label = "") {
        Write(GetIndent());
        WriteLine((string.IsNullOrEmpty(label) ? "[Group]" : $"[Group: {label}]"));
        _groupDepth++;
    }

    public static void groupEnd() {
        if (_groupDepth > 0)
            _groupDepth--;
    }

    public static void table(IEnumerable<object> data) {
        if (data == null || !data.Any()) {
            Write(GetIndent());
            WriteLine("(empty table)");
            return;
        }

        var properties = data.First().GetType().GetProperties();
        if (properties.Length == 0) {
            Write(GetIndent());
            WriteLine("(No properties found)");
            return;
        }

        // Print header
        var headers = properties.Select(p => p.Name).ToList();
        Write(GetIndent());
        WriteLine(string.Join(" | ", headers));
        Write(GetIndent());
        WriteLine(new string('-', headers.Sum(h => h.Length + 3)));

        // Print rows
        foreach (var row in data) {
            var values = properties.Select(p => p.GetValue(row, null)?.ToString() ?? "").ToList();
            Write(GetIndent());
            WriteLine(string.Join(" | ", values));
        }
    }

    public static void dump(object? data, string label = "Dump", int depth = 0) {
        if (depth > 10) {
            Write(GetIndent());
            WriteLine("[ERROR] Maximum recursion depth reached.");
            return;
        }

        Write(GetIndent());
        WriteLine($"[{label}]");
        Write(GetIndent());
        WriteLine(new string('-', label.Length + 2));

        dumpRecursive(data, depth);
    }

    private static void dumpRecursive(object? data, int depth) {
        if (data == null) {
            Write(GetIndent());
            WriteLine("null");
            return;
        }

        if (data is IDictionary<string, object> dict) {
            foreach (var kvp in dict) {
                Write(GetIndent());
                Write($"{kvp.Key}: ");

                if (kvp.Value is IDictionary<string, object> subDict) {
                    WriteLine();
                    _groupDepth++;
                    dumpRecursive(subDict, depth + 1);
                    _groupDepth--;
                }
                else if (kvp.Value is IEnumerable<object> list) {
                    WriteLine();
                    _groupDepth++;
                    foreach (var item in list) {
                        dumpRecursive(item, depth + 1);
                    }
                    _groupDepth--;
                }
                else {
                    WriteLine(formatValue(kvp.Value));
                }
            }
        }
        else if (data is IEnumerable<object> list) {
            foreach (var item in list) {
                dumpRecursive(item, depth + 1);
            }
        }
        else {
            WriteLine(GetIndent() + formatValue(data));
        }
    }

    private static string formatValue(object? value) {
        return value switch {
            null => "null",
            string str => $"\"{str}\"",
            IEnumerable<object> list => $"[{string.Join(", ", list.Select(formatValue))}]",
            _ => value.ToString() ?? "null"
        };
    }
}
