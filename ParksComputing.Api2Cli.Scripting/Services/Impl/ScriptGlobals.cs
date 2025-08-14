using ParksComputing.Api2Cli.Api;

namespace ParksComputing.Api2Cli.Scripting.Services.Impl
{
    // Public globals type used by Roslyn C# scripting so identifiers like 'a2c' are available.
    public class ScriptGlobals
    {
        public A2CApi a2c { get; set; } = default!;
    }
}
