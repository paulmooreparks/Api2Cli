using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using ParksComputing.Api2Cli.Orchestration.Services;
using ParksComputing.Api2Cli.Scripting.Services;

namespace ParksComputing.Api2Cli.Tests;

[TestClass]
public class OrchestratorInitScriptsTests
{
    private static ServiceProvider _sp = null!;

    [ClassInitialize]
    public static void Init(TestContext _)
    {
        _sp = (ServiceProvider)TestSetup.ConfigureServices();
    }

    [TestMethod]
    public void GroupedInitScripts_AreScopedByLanguage_AndClrFallbackExists()
    {
        var orch = _sp.GetRequiredService<IWorkspaceScriptingOrchestrator>();
        var js = _sp.GetRequiredService<IApi2CliScriptEngineFactory>().GetEngine("javascript");
        var cs = _sp.GetRequiredService<IApi2CliScriptEngineFactory>().GetEngine("csharp");

        orch.Initialize();

        // JS helper from JS init should exist
        var jsSum = js.EvaluateScript("addNumbers(5,7)");
        Assert.AreEqual(12d, System.Convert.ToDouble(jsSum));

        // CS helper from CS init should exist in C# engine
        var csSum = cs.EvaluateScript("addNumbers(5,7)");
        Assert.AreEqual(12d, System.Convert.ToDouble(csSum));

        // Default JS 'clr' should be present even if user doesn't define it
        var hasClr = js.EvaluateScript("typeof clr !== 'undefined'");
        Assert.AreEqual(true, System.Convert.ToBoolean(hasClr));
    }
}
