using System;
using System.Collections.Generic;
using System.Linq;

namespace ParksComputing.Api2Cli.Cli.Utilities;

internal static class CookieHeaderHelper {
    /// <summary>
    /// Merges provided cookies (name=value) into an existing headers collection, returning a new mutable list.
    /// Existing Cookie header (case-insensitive) will have values appended with "; ".
    /// Invalid cookie tokens (missing '=') are ignored.
    /// </summary>
    public static List<string> MergeCookies(IEnumerable<string>? headers, IEnumerable<string>? cookies) {
        var headerList = headers is not null ? new List<string>(headers) : new List<string>();

        if (cookies is null) {
            return headerList;
        }

        var cookiePairs = cookies
            .Where(c => !string.IsNullOrWhiteSpace(c) && c.Contains('='))
            .Select(c => c.Trim())
            .ToList();

        if (cookiePairs.Count == 0) {
            return headerList;
        }

        var newCookieValue = string.Join("; ", cookiePairs);
        var existingIndex = headerList.FindIndex(h => h.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0) {
            var existing = headerList[existingIndex];
            var parts = existing.Split(':', 2);
            var existingCookies = parts.Length == 2 ? parts[1].Trim() : string.Empty;
            var merged = string.IsNullOrEmpty(existingCookies) ? newCookieValue : existingCookies + "; " + newCookieValue;
            headerList[existingIndex] = "Cookie: " + merged;
        }
        else {
            headerList.Add("Cookie: " + newCookieValue);
        }

        return headerList;
    }
}
