namespace GostGen.Tests;

using GostGen.DTO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Threading.Tasks;

[TestClass]
public class GostBypassSyncTest
{
    [TestMethod]
    public async Task UpdateAsync()
    {
        var testCases = new List<(List<string> GatewayBypasses, List<string> GostBypasses, bool ExpectChange)>
            {
                ([], [], false),
                (["test.com"], [], true),
                ([], ["test.com"], true),
                (["test.com"], ["test.com"], false),
                (["test.com", "test2.com"], ["test2.com", "test.com"], false),
                (["test.com"], ["test.com", "test.com"], false),
                (["test.com", "test.com"], ["test.com"], false),
            };

        foreach (var testCase in testCases)
        {
            var gostConfig = new GostConfig { Bypasses = [new() { Name = GostBypassSync.BypassMullvadGroup, Matchers = testCase.GostBypasses }] };
            var gatewayConfig = new GatewayConfig { Bypasses = testCase.GatewayBypasses };
            var result = await GostBypassSync.UpdateAsync(gostConfig, gatewayConfig);
            Assert.AreEqual(testCase.ExpectChange, result, "Bypass config change detection failed");
        }
    }
}