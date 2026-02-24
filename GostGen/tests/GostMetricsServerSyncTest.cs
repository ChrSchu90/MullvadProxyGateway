namespace GostGen.Tests;

using GostGen.DTO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

[TestClass]
public class GostMetricsServerSyncTest
{
    [TestMethod]
    [DataRow(true, null, null, null, true, DisplayName = "Enable metrics server when Metrics is null")]
    [DataRow(true, GostMetricsServerSync.MetricsPort, GostMetricsServerSync.MetricsPath, GostUserSync.AutherMetricsGroup, false, DisplayName = "No change when Metrics is already configured correctly")]
    [DataRow(true, GostMetricsServerSync.MetricsPort + 1, GostMetricsServerSync.MetricsPath, GostUserSync.AutherMetricsGroup, true, DisplayName = "Update Metrics address when incorrect")]
    [DataRow(true, GostMetricsServerSync.MetricsPort, "/wrong-path", GostUserSync.AutherMetricsGroup, true, DisplayName = "Update Metrics path when incorrect")]
    [DataRow(true, GostMetricsServerSync.MetricsPort, GostMetricsServerSync.MetricsPath, "wrong-auther", true, DisplayName = "Update Metrics auther when incorrect")]
    [DataRow(false, GostMetricsServerSync.MetricsPort, GostMetricsServerSync.MetricsPath, GostUserSync.AutherMetricsGroup, true, DisplayName = "Disable metrics server when GostMetricsEnabled is false")]
    [DataRow(false, null, null, null, false, DisplayName = "No change when Metrics is already null and GostMetricsEnabled is false")]
    public async Task UpdateAsync(bool gostMetricsEnabled, int? port, string? initialPath, string? initialAuther, bool expectedChanged)
    {
        var initialAddr = port.HasValue ? $":{port.Value}" : null;
        var gostConfig = new GostConfig { Metrics = initialAddr == null ? null : new MetricsConfig { Addr = initialAddr, Path = initialPath, Auther = initialAuther } };
        var gatewayConfig = new GatewayConfig { GostMetricsEnabled = gostMetricsEnabled };

        var result = await GostMetricsServerSync.UpdateAsync(gostConfig, gatewayConfig);
        Assert.AreEqual(expectedChanged, result);
        if (gostMetricsEnabled)
        {
            Assert.IsNotNull(gostConfig.Metrics);
            Assert.AreEqual($":{GostMetricsServerSync.MetricsPort}", gostConfig.Metrics.Addr);
            Assert.AreEqual(GostMetricsServerSync.MetricsPath, gostConfig.Metrics.Path);
            Assert.AreEqual(GostUserSync.AutherMetricsGroup, gostConfig.Metrics.Auther);
        }
        else
        {
            Assert.IsNull(gostConfig.Metrics);
        }
    }
}