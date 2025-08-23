using System;
using System.Linq;

namespace ParksComputing.Api2Cli.Cli.Commands.WorkspaceTools;

internal static class WorkspaceImportHelpers {
    private static bool IsScriptDebugEnabled() => string.Equals(Environment.GetEnvironmentVariable("A2C_SCRIPT_DEBUG"), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable("A2C_SCRIPT_DEBUG"), "1", StringComparison.OrdinalIgnoreCase);
    private static void DebugLog(string message, Exception? ex = null) {
        if (!IsScriptDebugEnabled()) { return; }
        try {
            System.Console.Error.WriteLine(ex is null ? message : message + " :: " + ex.GetType().Name + ": " + ex.Message);
        } catch { }
    }
    internal static string GetRelativePath(string baseDir, string target) {
        if (string.IsNullOrWhiteSpace(baseDir) || string.IsNullOrWhiteSpace(target)) {
            return target;
        }

        try {
            var baseFull = System.IO.Path.GetFullPath(baseDir);
            var targetFull = System.IO.Path.GetFullPath(target);

            var baseRoot = System.IO.Path.GetPathRoot(baseFull);
            var targetRoot = System.IO.Path.GetPathRoot(targetFull);
            if (!string.Equals(baseRoot, targetRoot, StringComparison.OrdinalIgnoreCase)) {
                return targetFull; // different volume; keep absolute
            }

            static string EnsureTrailingSeparator(string p) =>
                p.EndsWith(System.IO.Path.DirectorySeparatorChar) || p.EndsWith(System.IO.Path.AltDirectorySeparatorChar)
                    ? p
                    : p + System.IO.Path.DirectorySeparatorChar;

            var baseUri = new Uri(EnsureTrailingSeparator(baseFull), UriKind.Absolute);
            var targetUri = new Uri(targetFull, UriKind.Absolute);
            var relUri = baseUri.MakeRelativeUri(targetUri);
            if (relUri.IsAbsoluteUri) { return targetFull; }

            var rel = Uri.UnescapeDataString(relUri.ToString()); // '/' separators
            if (string.IsNullOrWhiteSpace(rel) || rel.StartsWith("../", StringComparison.Ordinal)) {
                return targetFull;
            }
            return rel;
        }
    catch (Exception ex) { DebugLog($"[WorkspaceImport] GetRelativePath failed for '{target}'", ex); return target; }
    }

    internal static string MakeRequestName(string method, string path, string? operationId) {
        if (!string.IsNullOrWhiteSpace(operationId)) { return operationId; }
        var p = (path ?? string.Empty).Trim();
        if (p.StartsWith("/")) { p = p[1..]; }
        var chars = p.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var baseName = new string(chars);
        if (string.IsNullOrWhiteSpace(baseName)) { baseName = "root"; }
        return $"{method.ToLowerInvariant()}_{baseName}";
    }
}
