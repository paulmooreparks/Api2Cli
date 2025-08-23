using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ParksComputing.Api2Cli.Diagnostics.Services.Unified.Impl;

internal interface IDiagnosticsSink { void Publish(DiagnosticEvent evt); }

internal sealed class XferLangDiagnosticsSink : IDiagnosticsSink {
    private readonly TextWriter _writer;
    private readonly object _lock = new();
    public XferLangDiagnosticsSink(TextWriter writer) { _writer = writer; }
    public void Publish(DiagnosticEvent evt) {
        var sb = new StringBuilder();
        sb.Append("diag {");
        Append(sb, "ts", evt.Timestamp.UtcDateTime.ToString("o"));
        Append(sb, "sev", evt.Severity.ToString());
        Append(sb, "cat", evt.Category);
        if (!string.IsNullOrEmpty(evt.Code)) {
            Append(sb, "code", evt.Code!);
        }
        Append(sb, "msg", evt.Message);
        if (evt.Exception is not null) {
            Append(sb, "exType", evt.Exception.GetType().Name);
            Append(sb, "exMsg", evt.Exception.Message);
        }
        foreach (var kv in evt.Context) {
            if (kv.Value is null) {
                continue;
            }
            Append(sb, kv.Key, kv.Value);
        }
        sb.Append('}');
        lock (_lock) { _writer.WriteLine(sb.ToString()); }
    }
    private static void Append(StringBuilder sb, string key, object value) {
        sb.Append(' ').Append(key).Append(' ');
        switch (value) {
            case string s:
                sb.Append('"').Append(Escape(s)).Append('"');
                break;
            case bool b:
                sb.Append(b ? "~true" : "~false");
                break;
            case Enum e:
                sb.Append(e.ToString());
                break;
            default:
                if (value is IFormattable f) {
                    sb.Append(f.ToString(null, System.Globalization.CultureInfo.InvariantCulture));
                }
                else {
                    sb.Append('"').Append(Escape(value.ToString() ?? string.Empty)).Append('"');
                }
                break;
        }
    }
    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
