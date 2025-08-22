using System.Collections.Generic;

namespace ParksComputing.Api2Cli.Workspace.Services;

public static class SlugUtil {
    public static string ToSlug(string name) {
        if (string.IsNullOrWhiteSpace(name)) { return "workspace"; }
        var chars = new List<char>(name.Length);
        bool lastDash = false;
        foreach (var ch in name.Trim().ToLowerInvariant()) {
            if (char.IsLetterOrDigit(ch)) { chars.Add(ch); lastDash = false; }
            else if (ch == '-' || ch == ' ' || ch == '_' || ch == '.' || ch == '/') {
                if (!lastDash) { chars.Add('-'); lastDash = true; }
            }
        }
    if (chars.Count == 0) { return "workspace"; }
    if (chars[^1] == '-') { chars.RemoveAt(chars.Count - 1); }
        return new string(chars.ToArray());
    }
}
