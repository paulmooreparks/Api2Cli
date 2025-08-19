using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.Orchestration.Services;
using System;

namespace ParksComputing.Api2Cli.Tests;

[TestClass]
public class CSharpTypedArgsTests
{
    private static ServiceProvider _sp = null!;

    [ClassInitialize]
    public static void Init(TestContext _)
    {
        _sp = (ServiceProvider)TestSetup.ConfigureServices();
        // Ensure engines are initialized and wrappers are registered
        var orch = _sp.GetRequiredService<IWorkspaceScriptingOrchestrator>();
        orch.Initialize();
    }

    [TestMethod]
    public void Sum_Int32_Args()
    {
        var factory = _sp.GetRequiredService<IApi2CliScriptEngineFactory>();
        var js = factory.GetEngine("javascript");
        var result = js.EvaluateScript("a2c.cs_sum_ints(2, 3)");
        Assert.AreEqual(5d, Convert.ToDouble(result));
    }

    [TestMethod]
    public void Guid_Arg_String_To_Guid()
    {
        var factory = _sp.GetRequiredService<IApi2CliScriptEngineFactory>();
        var js = factory.GetEngine("javascript");
        var g = Guid.NewGuid();
        var result = js.EvaluateScript($"a2c.cs_guid_str('{g}')");
        Assert.AreEqual(g.ToString(), Convert.ToString(result));
    }

    [TestMethod]
    public void Enum_Arg_DayOfWeek()
    {
        var factory = _sp.GetRequiredService<IApi2CliScriptEngineFactory>();
        var js = factory.GetEngine("javascript");
        var result = js.EvaluateScript("a2c.cs_day_enum('Friday')");
        // Friday => 5
        Assert.AreEqual(5d, Convert.ToDouble(result));
    }

    [TestMethod]
    public void DateTime_Roundtrip_ISO()
    {
        var factory = _sp.GetRequiredService<IApi2CliScriptEngineFactory>();
        var js = factory.GetEngine("javascript");
        var iso = DateTime.UtcNow.ToString("O");
        var result = js.EvaluateScript($"a2c.cs_dt_roundtrip('{iso}')");
        Assert.AreEqual(iso, Convert.ToString(result));
    }

    [TestMethod]
    public void Int32_Array_From_Json_String()
    {
        var factory = _sp.GetRequiredService<IApi2CliScriptEngineFactory>();
        var js = factory.GetEngine("javascript");
        var result = js.EvaluateScript("a2c.cs_array_sum('[1,2,3,4]')");
        Assert.AreEqual(10d, Convert.ToDouble(result));
    }

    [TestMethod]
    public void Uri_Arg_Host()
    {
        var factory = _sp.GetRequiredService<IApi2CliScriptEngineFactory>();
        var js = factory.GetEngine("javascript");
        var result = js.EvaluateScript("a2c.cs_uri_host('https://example.com/path?q=1')");
        Assert.AreEqual("example.com", Convert.ToString(result));
    }

    [TestMethod]
    public void Json_To_Dictionary_String_Object()
    {
        var factory = _sp.GetRequiredService<IApi2CliScriptEngineFactory>();
        var js = factory.GetEngine("javascript");
        var result = js.EvaluateScript("a2c.cs_json_dict_name('{\"name\":\"Ada\", \"age\": 42}')");
        Assert.AreEqual("Ada", Convert.ToString(result));
    }
}
