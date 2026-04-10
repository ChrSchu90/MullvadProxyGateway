namespace GostGen.Tests;

using GostGen.DTO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

/// <summary>
/// Test for <see cref="GostMetricsServerSync"/>
/// </summary>
[TestClass]
public class GostMetricsServerSyncTests
{
    private const int TestMetricsPort = 1234;

    /// <summary>
    /// Tests for <see cref="GostMetricsServerSync.UpdateAsync"/>
    /// </summary>
    [TestMethod]
    [DataRow(true, null, null, null, true, true, DisplayName = "Enable metrics server when Metrics is null")]
    [DataRow(true, TestMetricsPort, GostMetricsServerSync.MetricsPath, GostUserSync.AutherMetricsGroup, false, true, DisplayName = "No change when Metrics is already configured correctly")]
    [DataRow(true, TestMetricsPort + 1, GostMetricsServerSync.MetricsPath, GostUserSync.AutherMetricsGroup, true, true, DisplayName = "Update Metrics address when incorrect")]
    [DataRow(true, TestMetricsPort, "/wrong-path", GostUserSync.AutherMetricsGroup, true, true, DisplayName = "Update Metrics path when incorrect")]
    [DataRow(true, TestMetricsPort, GostMetricsServerSync.MetricsPath, "wrong-auther", true, true, DisplayName = "Update Metrics auther when incorrect")]
    [DataRow(false, TestMetricsPort, GostMetricsServerSync.MetricsPath, GostUserSync.AutherMetricsGroup, true, true, DisplayName = "Disable metrics server when GostMetricsEnabled is false")]
    [DataRow(false, null, null, null, false, true, DisplayName = "No change when Metrics is already null and GostMetricsEnabled is false")]
    [DataRow(true, null, null, null, true, true, DisplayName = "Enabled user role")]
    [DataRow(true, null, null, GostUserSync.AutherMetricsGroup, true, false, DisplayName = "Disabled user role")]
    [DataRow(true, null, null, null, true, null, DisplayName = "None user role")]
    public async Task UpdateAsync(bool gostMetricsEnabled, int? port, string? initialPath, string? initialAuther, bool expectedChanged, bool? userRole)
    {
        var initialAddr = port.HasValue ? $":{port.Value}" : null;
        var gostConfig = new GostConfig { Metrics = initialAddr == null ? null : new MetricsConfig { Addr = initialAddr, Path = initialPath, Auther = initialAuther } };
        var gatewayConfig = new GatewayConfig { GostMetricsEnabled = gostMetricsEnabled, GostMetricsPort = TestMetricsPort };
        gatewayConfig.Users.Add("test", new User { Password = "testpw", HasMetricsAccess = userRole});

        var result = await GostMetricsServerSync.UpdateAsync(gostConfig, gatewayConfig);
        Assert.AreEqual(expectedChanged, result);
        if (gostMetricsEnabled)
        {
            Assert.IsNotNull(gostConfig.Metrics);
            Assert.AreEqual($":{gatewayConfig.GostMetricsPort}", gostConfig.Metrics.Addr);
            Assert.AreEqual(GostMetricsServerSync.MetricsPath, gostConfig.Metrics.Path);
            Assert.AreEqual(userRole.HasValue ? GostUserSync.AutherMetricsGroup : null, gostConfig.Metrics.Auther);
        }
        else
        {
            Assert.IsNull(gostConfig.Metrics);
        }
    }
}