using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace ParksComputing.Api2Cli.Tests;

[TestClass]
public class WorkspaceImportCliTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static string CliProjectPath => Path.Combine(RepoRoot, "a2c", "a2c.csproj");

    private static string QuoteArg(string a)
        => a.Any(char.IsWhiteSpace) || a.Contains('"') ? "\"" + a.Replace("\"", "\\\"") + "\"" : a;

    private static (int ExitCode, string StdOut, string StdErr) RunCli(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{CliProjectPath}\" -- {string.Join(" ", args.Select(QuoteArg))}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepoRoot
        };

        using var proc = Process.Start(psi)!;
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => {
            if (e.Data != null) {
                stdout.AppendLine(e.Data);
            }
        };
        proc.ErrorDataReceived += (_, e) => {
            if (e.Data != null) {
                stderr.AppendLine(e.Data);
            }
        };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit(120_000);
        return (proc.ExitCode, stdout.ToString(), stderr.ToString());
    }

    [TestMethod]
    public void ImportFromList_CreatesWorkspaceAndRequests()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            // With directory-root semantics, --config points to a folder that contains config.xfer and workspaces/
            var configRoot = tempDir.FullName;
            var listPath = Path.Combine(tempDir.FullName, "spec.txt");
            File.WriteAllText(listPath, "GET /pets\nPOST /pets\nPUT /pets/{id}\n# comment\n");

            var (code, stdout, stderr) = RunCli(
                "--config", configRoot,
                "workspace", "import-list",
                "--name", "imported",
                "--spec", listPath,
                "--baseurl", "https://api.example.com",
                "--force"
            );

            Assert.AreEqual(0, code, $"CLI failed: {stderr}\n{stdout}");
            var wsDir = Path.Combine(configRoot, "imported");
            var wsFile = Path.Combine(wsDir, "workspace.xfer");
            Assert.IsTrue(File.Exists(wsFile), "workspace.xfer should have been created");
            var content = File.ReadAllText(wsFile);
            StringAssert.Contains(content, "requests");
            StringAssert.Contains(content, "get_pets");
            StringAssert.Contains(content, "post_pets");
        }
        finally
        {
            try { tempDir.Delete(true); } catch { /* best effort */ }
        }
    }

    [TestMethod]
    public void ImportOpenApiJson_CreatesWorkspaceFromSpec()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            // With directory-root semantics, --config points to a folder that contains config.xfer and workspaces/
            var configRoot = tempDir.FullName;
            var specPath = Path.Combine(tempDir.FullName, "openapi.json");
            var json = """
            {
              "openapi": "3.0.0",
              "servers": [ { "url": "https://api.example.com" } ],
              "paths": {
                "/pets": {
                  "get": { "operationId": "listPets", "summary": "List pets" },
                  "post": { "summary": "Create pet" }
                }
              }
            }
            """;
            File.WriteAllText(specPath, json);

            var (code, stdout, stderr) = RunCli(
                "--config", configRoot,
                "workspace", "import",
                "--name", "pets",
                "--openapi", specPath,
                "--force"
            );

            Assert.AreEqual(0, code, $"CLI failed: {stderr}\n{stdout}");
            var wsDir = Path.Combine(configRoot, "pets");
            var wsFile = Path.Combine(wsDir, "workspace.xfer");
            Assert.IsTrue(File.Exists(wsFile), "workspace.xfer should have been created");
            var content = File.ReadAllText(wsFile);
            StringAssert.Contains(content, "listPets");
            StringAssert.Contains(content, "post_pets");
            StringAssert.Contains(content, "baseUrl \"https://api.example.com\"");
        }
        finally
        {
            try { tempDir.Delete(true); } catch { /* best effort */ }
        }
    }
}
