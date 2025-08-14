using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.ClearScript;

namespace ParksComputing.Api2Cli.Scripting.Api.Process;

public interface IProcessApi
{
    [ScriptMember("run")]
    void Run(string? command, string? workingDirectory, params string[] arguments);
    [ScriptMember("run")]
    void Run(string? command, string? workingDirectory);
    [ScriptMember("run")]
    void Run(string? command);
    [ScriptMember("runCommand")]
    string RunCommand(bool captureOutput, string? workingDirectory, string command, params string[]? args);
}
