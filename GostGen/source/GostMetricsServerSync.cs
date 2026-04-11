namespace GostGen;

using GostGen.DTO;
using Serilog;
using System.Threading.Tasks;

/// <summary>
/// Synchronizes the <see cref="GostConfig"/> metrics server configuration with the <see cref="GatewayConfig"/>
/// </summary>
internal class GostMetricsServerSync
{
    internal const string MetricsPath = "/metrics";

    /// <summary>
    /// Updates the metrics server inside the <see cref="GostConfig"/>.
    /// </summary>
    /// <param name="gostConfig">The GOST configuration.</param>
    /// <param name="gatewayConfig">The gateway configuration.</param>
    /// <returns><c>true</c> if the <see cref="GostConfig"/> has been changed.</returns>
    internal static Task<bool> UpdateAsync(GostConfig gostConfig, GatewayConfig gatewayConfig)
    {
        var changed = false;
        if (gatewayConfig.GostMetricsEnabled)
        {
            var metricsAddress = $":{gatewayConfig.GostMetricsPort}";
            gostConfig.Metrics ??= new();
            var autherGrp = gatewayConfig.HasMetricsAccessUser ? GostUserSync.AutherMetricsGroup : null;
            if (gostConfig.Metrics.Addr != metricsAddress ||
                !string.Equals(gostConfig.Metrics.Path, MetricsPath) ||
                !string.Equals(gostConfig.Metrics.Auther, autherGrp))
            {
                Log.Debug($"Enable metrics server, use `curl -v -u user:passwd http://ip:{gatewayConfig.GostMetricsPort}{MetricsPath}` for tests");
                gostConfig.Metrics.Addr = metricsAddress;
                gostConfig.Metrics.Path = MetricsPath;
                gostConfig.Metrics.Auther = autherGrp;
                changed = true;
            }
        }
        else if (gostConfig.Metrics != null)
        {
            Log.Debug("Disable metrics server");
            gostConfig.Metrics = null;
            changed = true;
        }

        return Task.FromResult(changed);
    }
}
