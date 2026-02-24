namespace GostGen;

using GostGen.DTO;
using Serilog;
using System.Threading.Tasks;

/// <summary>
/// Synchronizes the <see cref="GostConfig"/> metrics server configuration with the <see cref="GatewayConfig"/>
/// </summary>
internal class GostMetricsServerSync
{
    internal const int MetricsPort = 9100;
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
            var metricsAddress = $":{MetricsPort}";
            gostConfig.Metrics ??= new();
            if (gostConfig.Metrics.Addr != metricsAddress ||
                !string.Equals(gostConfig.Metrics.Path, MetricsPath) ||
                !string.Equals(gostConfig.Metrics.Auther, GostUserSync.AutherMetricsGroup))
            {
                Log.Debug($"Enable metrics server, use `curl -v -u user:passwd http://ip:{MetricsPort}{MetricsPath}` for tests");
                gostConfig.Metrics.Addr = metricsAddress;
                gostConfig.Metrics.Path = MetricsPath;
                gostConfig.Metrics.Auther = GostUserSync.AutherMetricsGroup;
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
