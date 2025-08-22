using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace ParksComputing.Api2Cli.Tests;

[TestClass]
public class CliConfigArgTests
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
            if (e.Data != null) { stdout.AppendLine(e.Data); }
        };
        proc.ErrorDataReceived += (_, e) => {
            if (e.Data != null) { stderr.AppendLine(e.Data); }
        };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit(120_000);
        return (proc.ExitCode, stdout.ToString(), stderr.ToString());
    }

    [TestMethod]
    public void ConfigArg_FilePath_FailsWithClearMessage()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var fileLikeConfig = Path.Combine(tempDir.FullName, "workspaces.xfer");
            File.WriteAllText(fileLikeConfig, "# sentinel\n");

            var (code, stdout, stderr) = RunCli(
                "--config", fileLikeConfig,
                // Use a command that triggers DI/service construction
                "workspace", "import-list",
                "--name", "noop",
                "--spec", fileLikeConfig,
                "--force"
            );

            Assert.AreNotEqual(0, code, $"CLI should fail when --config points to a file. Output:\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
            StringAssert.Contains(stderr, "--config must be a directory", "Error should explain that --config must be a directory");
        }
        finally
        {
            try { tempDir.Delete(true); } catch { /* best effort */ }
        }
    }
}
