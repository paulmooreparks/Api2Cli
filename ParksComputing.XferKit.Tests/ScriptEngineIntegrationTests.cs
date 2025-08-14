using Microsoft.VisualStudio.TestTools.UnitTesting;
using ParksComputing.Api2Cli.Scripting.Services;
using System;
using System.IO;

namespace ParksComputing.Api2Cli.Tests
{
    [TestClass]
    public class ScriptEngineIntegrationTests
    {
        private static IServiceProvider _serviceProvider = null!;

        [ClassInitialize]
        public static void ClassInitialize(TestContext context)
        {
            _serviceProvider = TestSetup.ConfigureServices();
        }

        [TestMethod]
        public void CSharpScriptAccessesFacilitiesTest()
        {
            // Get the IXferScriptEngineFactory from the service provider
            var factory = _serviceProvider.GetService(typeof(IXferScriptEngineFactory)) as IXferScriptEngineFactory;
            Assert.IsNotNull(factory, "IXferScriptEngineFactory should not be null.");

            // Get the CSharpScriptEngine instance using the "csharp" key
            var scriptEngine = factory.GetEngine("csharp");
            Assert.IsNotNull(scriptEngine, "IXferScriptEngine should not be null.");

            // Define a script that accesses facilities
            string script = """
                Console.WriteLine("Accessing Console");
                // var result =\ a2c.Http.get("https://example.com", null, null);
                // return result != null ? "Http Access Successful" : "Http Access Failed";
            """;

            // Execute the script
            string result = scriptEngine.ExecuteScript(script);

            // Assert the result
            Assert.AreEqual("Http Access Successful", result, "The script did not access facilities as expected.");
        }

        [TestMethod]
        public void CSharpScriptAccessesXkTest()
        {
            // Get the IXferScriptEngineFactory from the service provider
            var factory = _serviceProvider.GetService(typeof(IXferScriptEngineFactory)) as IXferScriptEngineFactory;
            Assert.IsNotNull(factory, "IXferScriptEngineFactory should not be null.");

            // Get the CSharpScriptEngine instance using the "csharp" key
            var scriptEngine = factory.GetEngine("csharp");
            Assert.IsNotNull(scriptEngine, "IXferScriptEngine should not be null.");

            // Define a simple script to test access to xk
            string script = "return xk != null ? \"xk is accessible\" : \"xk is not accessible\";";

            // Execute the script
            string result = scriptEngine.ExecuteScript(script);

            // Assert the result
            Assert.AreEqual("xk is accessible", result, "The script could not access the xk object.");
        }
    }
}
