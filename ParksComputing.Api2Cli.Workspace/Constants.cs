using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParksComputing.Api2Cli.Workspace;

public static class Constants {
    public const string Api2CliDiagnosticsName = "Api2Cli";
    public const string Api2CliDirectoryName = ".a2c";
    public const string WorkspacesFileName = "config.xfer";
    public const string StoreFileName = "store.sqlite";
    public const string EnvironmentFileName = ".env";
    public const string PackageDirName = "packages";
    public const string ScriptFilePrefix = "file:";
    public static int ScriptFilePrefixLength = ScriptFilePrefix.Length;
    public const string MutexName = "Global\\A2CMutex"; // Global mutex name for cross-process synchronization

    public const string SuccessChar = "✅";
    public const string WarningChar = "⚠️";
    public const string ErrorChar = "❌";
}
