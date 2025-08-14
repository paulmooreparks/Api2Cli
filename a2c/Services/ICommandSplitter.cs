namespace ParksComputing.Api2Cli.Cli.Services;

internal interface ICommandSplitter
{
    IEnumerable<string> Split(string commandLine);
}