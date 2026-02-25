namespace GostGen.Tests;

using GostGen.DTO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Test for <see cref="GostBypassSync"/>
/// </summary>
[TestClass]
public class GostBypassSyncTests
{
    /// <summary>
    /// Tests for <see cref="GostBypassSync.UpdateAsync"/>
    /// </summary>
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
        
        var gostConfig = new GostConfig();
        var gatewayConfig = new GatewayConfig { Bypasses = ["test"] };
        var result = await GostBypassSync.UpdateAsync(gostConfig, gatewayConfig);
        Assert.AreEqual(true, result, "Bypass config change detection failed");
        
        foreach (var testCase in testCases)
        {
            gostConfig = new GostConfig { Bypasses = [new() { Name = GostBypassSync.BypassMullvadGroup, Matchers = testCase.GostBypasses }] };
            gatewayConfig = new GatewayConfig { Bypasses = testCase.GatewayBypasses };
            result = await GostBypassSync.UpdateAsync(gostConfig, gatewayConfig);
            Assert.AreEqual(testCase.ExpectChange, result, "Bypass config change detection failed");
            
            
            gatewayConfig.Bypasses.Add("something.else.to.trigger.update");
            result = await GostBypassSync.UpdateAsync(gostConfig, gatewayConfig);
            Assert.AreEqual(true, result, "Bypass config change detection failed");
        }
    }
}