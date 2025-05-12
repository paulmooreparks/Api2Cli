using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using ParksComputing.XferKit.Scripting.Api.Process;

namespace ParksComputing.XferKit.Scripting.Api.Process.Impl;
internal class ProcessApi : IProcessApi
{
    public void Run(string? command, string? workingDirectory, params string[]? args)
    {
        var arguments = string.Empty;

        if (args is not null)
        {
            arguments = string.Join(" ", args);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? ".",
            UseShellExecute = true // Required to start a new window
        };

        var process = System.Diagnostics.Process.Start(startInfo);
    }

    public void Run(string? command, string? workingDirectory)
    {
        Run(command, workingDirectory, null);
    }

    public void Run(string? command)
    {
        Run(command, null, null);
    }

    public string RunCommand(bool captureOutput, string? workingDirectory, string command, params string[]? args)
    {
        var arguments = string.Empty;

        if (args is not null)
        {
            arguments = string.Join(" ", args);
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = captureOutput,
            CreateNoWindow = false,
            WorkingDirectory = workingDirectory
        };

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            processStartInfo.WorkingDirectory = workingDirectory;
        }

        using var process = new System.Diagnostics.Process
        {
            StartInfo = processStartInfo
        };

        process.Start();

        string output = "";

        if (captureOutput)
        {
            output = process.StandardOutput.ReadToEnd();
        }

        process.WaitForExit();
        return output;
    }
}
