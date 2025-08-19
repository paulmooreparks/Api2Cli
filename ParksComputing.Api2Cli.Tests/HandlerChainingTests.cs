using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using ParksComputing.Api2Cli.Orchestration.Services;
using ParksComputing.Api2Cli.Scripting.Services;
using System;
using System.Collections.Generic;

namespace ParksComputing.Api2Cli.Tests;

[TestClass]
public class HandlerChainingTests
{
    private static ServiceProvider _sp = null!;

    [ClassInitialize]
    public static void Init(TestContext _)
    {
        _sp = (ServiceProvider)TestSetup.ConfigureServices();
        var orch = _sp.GetRequiredService<IWorkspaceScriptingOrchestrator>();
        orch.Initialize();
    }

    [TestMethod]
    public void JavaScript_Base_Chaining_Sets_Both_Headers_And_Post_Appends()
    {
        var factory = _sp.GetRequiredService<IApi2CliScriptEngineFactory>();
        var js = factory.GetEngine("javascript");

        // Prepare inputs for preRequest
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parameters = new List<string>();
        string? payload = null;
        var cookies = new Dictionary<string, string>();
        object?[] extra = Array.Empty<object?>();

        var orch = _sp.GetRequiredService<IWorkspaceScriptingOrchestrator>();
        orch.InvokePreRequest("jschild", "echo", headers, parameters, ref payload, cookies, extra);

        Assert.AreEqual("1", headers["X-Base-JS"], "Base JS preRequest should set header");
        Assert.AreEqual("1", headers["X-Child-JS"], "Child JS preRequest should set header");

        // For postResponse, the JS chain should append markers in order base -> child
        var body = "start";
        var result = orch.InvokePostResponse("jschild", "echo", 200, null!, body, extra);
        Assert.AreEqual("start|baseJs|childJs", Convert.ToString(result));
    }

    [TestMethod]
    public void CSharp_Base_Chaining_Sets_Both_Headers_And_Payload_And_Post_Appends()
    {
        var factory = _sp.GetRequiredService<IApi2CliScriptEngineFactory>();
        var js = factory.GetEngine("javascript");

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parameters = new List<string>();
        string? payload = "start";
        var cookies = new Dictionary<string, string>();
        object?[] extra = Array.Empty<object?>();

        var orch = _sp.GetRequiredService<IWorkspaceScriptingOrchestrator>();
        orch.InvokePreRequest("cschild", "echo", headers, parameters, ref payload, cookies, extra);

        Assert.AreEqual("1", headers["X-Base-CS"], "Base C# preRequest should set header");
        Assert.AreEqual("1", headers["X-Child-CS"], "Child C# preRequest should set header");
        Assert.AreEqual("start|baseCs|childCs", payload, "Payload should be modified by base then child C# preRequest");

        var body = "resp";
        var result = orch.InvokePostResponse("cschild", "echo", 200, null!, body, extra);
        Assert.AreEqual("resp|baseCs|childCs", Convert.ToString(result));
    }
}
