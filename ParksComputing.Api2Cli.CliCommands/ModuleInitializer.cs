using System.Runtime.CompilerServices;

namespace ParksComputing.Api2Cli.CliCommands;

public class ModuleInitializer {
    public static void Initialize() {
        // Use unified diagnostics if available to avoid unsolicited stdout
        try {
            var svc = ParksComputing.Api2Cli.Cli.Services.Utility.GetService<ParksComputing.Api2Cli.Diagnostics.Services.Unified.IUnifiedDiagnostics>();
            svc?.Info("cli.module", "loaded", code: "module.loaded");
        } catch { /* swallow: initialization occurs before Utility provider set in some contexts */ }
    }
}
