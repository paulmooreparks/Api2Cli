using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Api2Cli.Diagnostics;
using ParksComputing.Api2Cli.Workspace.Models;

namespace ParksComputing.Api2Cli.Tests {
    [TestClass]
    public class EnvOverlayTests {
        private static IServiceProvider _sp = null!;
        private static string _tempRoot = string.Empty;

        [ClassInitialize]
        public static void ClassInitialize(TestContext ctx) {
            // Use a dedicated temp root per test class
            _tempRoot = Path.Combine(Path.GetTempPath(), "a2c-envtest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);

            var services = new ServiceCollection();
            services.AddSingleton(new WorkspaceRuntimeOptions { ConfigRoot = _tempRoot });
            services.AddApi2CliDiagnosticsServices("Api2CliTests");
            services.AddApi2CliWorkspaceServices();
            _sp = services.BuildServiceProvider();
        }

        [TestMethod]
        public void RootAndWorkspaceEnvOverlay_PrecedenceAndExpansion_Works() {
        // Unique variable names to avoid colliding with real environment
        const string BASE = "A2C_TEST_BASE";
        const string SAME = "A2C_TEST_SAME";
        const string CHAIN = "A2C_TEST_CHAIN";
        const string WSONLY = "A2C_TEST_WS_ONLY";

        // Arrange temp config root
        // Root .env
        File.WriteAllText(Path.Combine(_tempRoot, ".env"), $"{BASE}=base\n{SAME}=base\n{CHAIN}=${{{BASE}}}-root\n");

        // Workspace directory with its own .env overlay
        var wsDir = Path.Combine(_tempRoot, "work");
        Directory.CreateDirectory(wsDir);
        File.WriteAllText(Path.Combine(wsDir, ".env"), $"{SAME}=workspace\n{WSONLY}=only\n{CHAIN}=${{{BASE}}}-${{{SAME}}}\n");

        // Minimal config.xfer defining the workspace mapping
        File.WriteAllText(Path.Combine(_tempRoot, "config.xfer"), "{\n    workspaces {\n        test { dir \"work\" }\n    }\n}\n");

    var ws = _sp.GetRequiredService<IWorkspaceService>();

        // Assert root env applied with expansion
        Assert.AreEqual("base", Environment.GetEnvironmentVariable(BASE), "Root BASE not applied");
        Assert.AreEqual("base", Environment.GetEnvironmentVariable(SAME), "Root SAME not applied");
        Assert.AreEqual("base-root", Environment.GetEnvironmentVariable(CHAIN), "Root CHAIN expansion incorrect");
        Assert.IsNull(Environment.GetEnvironmentVariable(WSONLY), "Workspace-only var should not exist before activation");

        // Act: activate workspace overlay
        ws.SetActiveWorkspace("test");

        // Assert overlay precedence & expansion
        Assert.AreEqual("base", Environment.GetEnvironmentVariable(BASE), "BASE should remain from root");
        Assert.AreEqual("workspace", Environment.GetEnvironmentVariable(SAME), "Workspace SAME should override root");
        Assert.AreEqual("only", Environment.GetEnvironmentVariable(WSONLY), "Workspace-only var missing");
        Assert.AreEqual("base-workspace", Environment.GetEnvironmentVariable(CHAIN), "Workspace CHAIN expansion incorrect");

        // Act: reset to root ("/") to clear overlay
        ws.SetActiveWorkspace("/");

        // Assert restoration of root state
        Assert.AreEqual("base", Environment.GetEnvironmentVariable(BASE), "BASE after reset incorrect");
        Assert.AreEqual("base", Environment.GetEnvironmentVariable(SAME), "SAME should revert to root value after reset");
        Assert.AreEqual("base-root", Environment.GetEnvironmentVariable(CHAIN), "CHAIN should revert to root expansion");
        Assert.IsNull(Environment.GetEnvironmentVariable(WSONLY), "Workspace-only var should be removed after reset");

        // Cleanup explicitly (best-effort) to not leak env vars for other tests
        Environment.SetEnvironmentVariable(BASE, null);
        Environment.SetEnvironmentVariable(SAME, null);
        Environment.SetEnvironmentVariable(CHAIN, null);
        Environment.SetEnvironmentVariable(WSONLY, null);
        }
    }
}
