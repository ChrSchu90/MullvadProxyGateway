namespace GostGen;

using System;
using System.Linq;
using GostGen.DTO;
using Serilog;
using System.Threading.Tasks;

/// <summary>
/// Synchronizes the <see cref="GostConfig"/> bypass configuration with the <see cref="GatewayConfig"/>
/// </summary>
internal class GostBypassSync
{
    internal const string BypassMullvadGroup = "bypass-mullvad";
    
    /// <summary>
    /// Updates the bypasses inside the <see cref="GostConfig"/>.
    /// </summary>
    /// <param name="gostConfig">The GOST configuration.</param>
    /// <param name="gatewayConfig">The gateway configuration.</param>
    /// <returns><c>true</c> if the <see cref="GostConfig"/> has been changed.</returns>
    internal static Task<bool> UpdateAsync(GostConfig gostConfig, GatewayConfig gatewayConfig)
    {
        var changed = false;
        gostConfig.Bypasses ??= [];

        var bypassGroup = gostConfig.Bypasses.FirstOrDefault(a => string.Equals(a.Name, BypassMullvadGroup));
        if (bypassGroup == null)
        {
            Log.Debug($"Adding bypass group `{BypassMullvadGroup}`");
            bypassGroup = new BypassConfig { Name = BypassMullvadGroup };
            gostConfig.Bypasses.Add(bypassGroup);
            changed = true;
        }

        bypassGroup.Matchers ??= [];
        foreach (var configBypass in gatewayConfig.Bypasses)
        {
            var mullvadBypass = bypassGroup.Matchers.FirstOrDefault(u => string.Equals(u, configBypass, StringComparison.OrdinalIgnoreCase));
            if (mullvadBypass == null)
            {
                Log.Debug($"Add new bypass `{configBypass}` to `{BypassMullvadGroup}`");
                bypassGroup.Matchers.Add(configBypass);
                changed = true;
            }
        }

        foreach (var gostBypass in bypassGroup.Matchers.ToArray())
        {
            if (!string.IsNullOrWhiteSpace(gostBypass) &&
                gatewayConfig.Bypasses.Contains(gostBypass, StringComparer.OrdinalIgnoreCase))
                continue;

            Log.Debug($"Removing bypass `{gostBypass}` from `{BypassMullvadGroup}`");
            changed = bypassGroup.Matchers.Remove(gostBypass);
        }

        return Task.FromResult(changed);
    }
}
