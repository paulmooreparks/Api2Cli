using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParksComputing.XferKit.Scripting.Services;

using System;
using System.IO;

namespace ParksComputing.XferKit.Tests {
    [TestClass]
    public class ScriptEngineTests {
        private static IServiceProvider _serviceProvider = null!; // Use null-forgiving operator to suppress CS8618

        [ClassInitialize]
        public static void ClassInitialize(TestContext context) {
            _serviceProvider = TestSetup.ConfigureServices();
        }

        [TestMethod]
        public void ExecuteCSharpScriptTest() {
            // Get the IXferScriptEngineFactory from the service provider
            var factory = _serviceProvider.GetService(typeof(IXferScriptEngineFactory)) as IXferScriptEngineFactory;
            Assert.IsNotNull(factory, "IXferScriptEngineFactory should not be null.");

            // Get the CSharpScriptEngine instance using the "csharp" key
            var scriptEngine = factory.GetEngine("csharp");
            Assert.IsNotNull(scriptEngine, "IXferScriptEngine should not be null.");

            // Locate the script file in the output directory
            string scriptPath = Path.Combine(AppContext.BaseDirectory, "TestScripts", "SampleScript.csx");
            Assert.IsTrue(File.Exists(scriptPath), $"Script file should exist at {scriptPath}.");

            string scriptContent = File.ReadAllText(scriptPath);

            // Execute the script
            string result = scriptEngine.ExecuteScript(scriptContent);

            // Assert the result
            Assert.AreEqual("Hello, XferKit!", result, "The script did not return the expected result.");
        }
    }
}
