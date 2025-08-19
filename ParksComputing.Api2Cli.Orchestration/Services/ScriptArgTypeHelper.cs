using System;

namespace ParksComputing.Api2Cli.Orchestration.Services
{
    // A simple categorization of script argument types for client UIs and CLIs
    public enum ScriptArgKind
    {
        String,
        Number,
        Boolean,
        Object,
        Custom
    }

    public static class ScriptArgTypeHelper
    {
        // Map a type token from Xfer config (e.g., "string", "number", "System.Uri")
        // to a coarse-grained kind for UI/CLI. Custom means a specific CLR type handled by host conversion.
        public static ScriptArgKind GetArgKind(string? typeToken)
        {
            if (string.IsNullOrWhiteSpace(typeToken)) return ScriptArgKind.String;
            var t = typeToken.Trim();
            var lower = t.ToLowerInvariant();
            return lower switch
            {
                "string" or "system.string" => ScriptArgKind.String,
                "number" or "double" or "single" or "float" or "decimal" or "int" or "int32" or "int64" or "long" or "short" or "int16" or "uint" or "uint32" or "uint64" or "ulong" or "ushort" or "byte" or "sbyte" => ScriptArgKind.Number,
                "boolean" or "bool" or "system.boolean" => ScriptArgKind.Boolean,
                "object" or "system.object" => ScriptArgKind.Object,
                _ => ScriptArgKind.Custom
            };
        }
    }
}
