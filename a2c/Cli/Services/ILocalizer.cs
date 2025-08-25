namespace ParksComputing.Api2Cli.Cli.Services;

using System.Collections.Generic;
using System.Globalization;

// Simple localization abstraction. Keys map to resx entries. Context values substitute {tokens}.
public interface ILocalizer {
    string Get(string key, IReadOnlyDictionary<string, object?>? ctx = null, CultureInfo? culture = null);
}
