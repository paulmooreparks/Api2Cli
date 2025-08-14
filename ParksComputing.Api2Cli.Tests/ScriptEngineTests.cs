using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParksComputing.Api2Cli.Scripting.Services;

using System;
using System.IO;

namespace ParksComputing.Api2Cli.Tests {
    [TestClass]
    public class ScriptEngineTests {
        private static IServiceProvider _serviceProvider = null!; // Use null-forgiving operator to suppress CS8618

        [ClassInitialize]
        public static void ClassInitialize(TestContext context) {
            _serviceProvider = TestSetup.ConfigureServices();
        }

        [TestMethod]
        public void ExecuteCSharpScriptTest() {
            // Get the IApi2CliScriptEngineFactory from the service provider
            var factory = _serviceProvider.GetService(typeof(IApi2CliScriptEngineFactory)) as IApi2CliScriptEngineFactory;
            Assert.IsNotNull(factory, "IApi2CliScriptEngineFactory should not be null.");

            // Get the CSharpScriptEngine instance using the "csharp" key
            var scriptEngine = factory.GetEngine("csharp");
            Assert.IsNotNull(scriptEngine, "IApi2CliScriptEngine should not be null.");

            // Locate the script file in the output directory
            string scriptPath = Path.Combine(AppContext.BaseDirectory, "TestScripts", "SampleScript.csx");
            Assert.IsTrue(File.Exists(scriptPath), $"Script file should exist at {scriptPath}.");

            string scriptContent = File.ReadAllText(scriptPath);

            // Execute the script
            string result = scriptEngine.ExecuteScript(scriptContent);

            // Assert the result
            Assert.AreEqual("Hello, Api2Cli!", result, "The script did not return the expected result.");
        }
    }
}
