using System.Collections;
using System.Reflection;

namespace ParksComputing.XferKit.Scripting.Services;

public class ConsoleScriptObject {
    private static Dictionary<string, int> _counts = new();
    private static int _groupDepth = 0;

    private static string Indent => new string(' ', _groupDepth * 2);

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

    public static void log(params object[] args) {
        Write(Indent);
        WriteLine(args);
    }

    public static void info(params object[] args) {
        Write(Indent);
        Write("[INFO] ");
        WriteLine(args);
    }

    public static void warn(params object[] args) {
        Write(Indent);
        Write("[WARN] ");
        WriteLine(args);
    }

    public static void error(params object[] args) {
        Console.Error.Write(Indent);
        Console.Error.Write("[ERROR] ");
        Console.Error.WriteLine(args);
    }

    public static void debug(params object[] args) {
        Write(Indent);
        Write("[DEBUG] ");
        WriteLine(args);
    }

    public static void trace(params object[] args) {
        Write(Indent);
        Write("[TRACE] ");
        WriteLine(args);
        WriteLine(Environment.StackTrace);
    }

    public static void assert(bool condition, params object[] args) {
        if (!condition) {
            Console.Error.Write(Indent);
            Console.Error.Write("[ASSERT] ");
            Console.Error.WriteLine((args.Length > 0 ? args : "Assertion failed"));
        }
    }

    public static void count(string label = "default") {
        if (!_counts.ContainsKey(label)) {
            _counts[label] = 0;
        }
        _counts[label]++;
        Write(Indent);
        WriteLine($"{label}: {_counts[label]}");
    }

    public static void countReset(string label = "default") {
        if (_counts.ContainsKey(label)) {
            _counts[label] = 0;
        }
    }

    public static void group(string label = "") {
        Write(Indent);
        WriteLine((string.IsNullOrEmpty(label) ? "[Group]" : $"[Group: {label}]"));
        _groupDepth++;
    }

    public static void groupEnd() {
        if (_groupDepth > 0) {
            _groupDepth--;
        }
    }

    public static void table(IEnumerable<object> data) {
        if (data == null || !data.Any()) {
            Write(Indent);
            WriteLine("(empty table)");
            return;
        }

        var properties = data.First().GetType().GetProperties();
        if (properties.Length == 0) {
            Write(Indent);
            WriteLine("(No properties found)");
            return;
        }

        // Print header
        var headers = properties.Select(p => p.Name).ToList();
        Write(Indent);
        WriteLine(string.Join(" | ", headers));
        Write(Indent);
        WriteLine(new string('-', headers.Sum(h => h.Length + 3)));

        // Print rows
        foreach (var row in data) {
            var values = properties.Select(p => p.GetValue(row, null)?.ToString() ?? "").ToList();
            Write(Indent);
            WriteLine(string.Join(" | ", values));
        }
    }

    public static void dump(string label, object data, int depth = 0) {
        if (string.IsNullOrEmpty(label)) {
            label = "Dump";
        }

        if (depth > 10) {
            Write(Indent);
            WriteLine("[ERROR] Maximum recursion depth reached.");
            return;
        }

        var type = data.GetType();

        if (data is string || type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(DateTime)) {
            Write(Indent + $"{label} ");
            dumpRecursive(data, depth);
        }
        else {
            Write(Indent);
            WriteLine($"{label} ({type.Name}) {{");
            try {
                _groupDepth++;
                dumpRecursive(data, depth);
            }
            finally {
                _groupDepth--;
                Write(Indent);
                WriteLine("}");
            }
        }
    }

    private static void dumpRecursive(object? data, int depth) {
        if (data == null) {
            WriteLine(Indent + "null");
            return;
        }

        if (depth > 10) {
            WriteLine(Indent + "[Max depth]");
            return;
        }

        var type = data.GetType();

        if (data is string s) {
            WriteLine(Indent + $"({type.Name}): \"{s}\"");
            return;
        }

        if (type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(DateTime)) {
            WriteLine(Indent + $"({type.Name}): {data}");
            return;
        }

        if (data is IDictionary dict) {
            foreach (DictionaryEntry kvp in dict) {
                var keyType = kvp.Key?.GetType().Name ?? "unknown";
                var valType = kvp.Value?.GetType().Name ?? "unknown";

                Write(Indent);
                Write($"{kvp.Key} ({valType}): ");

                if (kvp.Value == null) {
                    WriteLine("null");
                }
                else {
                    WriteLine();
                    try {
                        _groupDepth++;
                        dumpRecursive(kvp.Value, depth + 1);
                    }
                    finally {
                        _groupDepth--;
                    }
                }
            }
            return;
        }

        if (data is IEnumerable enumerable && type != typeof(string)) {
            foreach (var item in enumerable) {
                dumpRecursive(item, depth + 1);
            }
            return;
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        if (properties.Length == 0) {
            WriteLine(Indent + data.ToString());
            return;
        }

        foreach (var prop in properties) {
            var value = prop.GetValue(data);
            var typeName = prop.PropertyType.Name;

            Write(Indent);
            Write($"{prop.Name} ({typeName}): ");

            if (value == null) {
                WriteLine("null");
            }
            else if (value is string || value.GetType().IsPrimitive || value is decimal || value is DateTime || value.GetType().IsEnum) {
                WriteLine(formatValue(value));
            }
            else {
                WriteLine();
                try {
                    _groupDepth++;
                    dumpRecursive(value, depth + 1);
                }
                finally {
                    _groupDepth--;
                }
            }
        }
    }

    private static string formatValue(object? value) {
        return value switch {
            null => "null",
            string s => $"\"{s}\"",
            IEnumerable enumerable when value is not string =>
                "[" + string.Join(", ", enumerable.Cast<object>().Select(formatValue)) + "]",
            _ => value.ToString() ?? "null"
        };
    }
}
