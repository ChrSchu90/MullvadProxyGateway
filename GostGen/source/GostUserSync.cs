namespace GostGen;

using System;
using System.Linq;
using System.Threading.Tasks;
using GostGen.DTO;
using Serilog;

/// <summary>
/// Synchronizes the <see cref="GostConfig"/> user configuration with the <see cref="GatewayConfig"/>
/// </summary>
internal class GostUserSync
{
    internal const string AutherMetricsGroup = "auther-metrics";
    internal const string AutherInternalGroup = "auther-internal";
    internal const string AutherMullvadGroup = "auther-mullvad";
    
    /// <summary>
    /// Updates the users inside the <see cref="GostConfig"/>.
    /// </summary>
    /// <param name="gostConfig">The GOST configuration.</param>
    /// <param name="gatewayConfig">The gateway configuration.</param>
    /// <returns><c>true</c> if the <see cref="GostConfig"/> has been changed.</returns>
    internal static Task<bool> UpdateAsync(GostConfig gostConfig, GatewayConfig gatewayConfig)
    {
        var changed = false;
        gostConfig.Authers ??= [];

        // Ensure all auther groups are available
        var mullvadGroup = AddAutherGroup(AutherMullvadGroup);
        mullvadGroup.Auths ??= [];
        var internalGroup = AddAutherGroup(AutherInternalGroup);
        internalGroup.Auths ??= [];
        var metricsGroup = AddAutherGroup(AutherMetricsGroup);
        metricsGroup.Auths ??= [];
        AutherConfig AddAutherGroup(string groupName)
        {
            var group = gostConfig.Authers!.FirstOrDefault(a => string.Equals(a.Name, groupName));
            if (group == null)
            {
                Log.Debug($"Adding auther group `{groupName}`");
                group = new AutherConfig { Name = groupName };
                gostConfig.Authers!.Add(group);
                changed = true;
            }

            return group;
        }

        // Add and update users inside auther groups that have the matching role
        foreach (var configUser in gatewayConfig.Users)
        {
            var auth = new AuthConfig { Username = configUser.Key, Password = configUser.Value.Password };
            AddAndUpdateUsers(mullvadGroup, () => configUser.Value.HasMullvadProxyAccess);
            AddAndUpdateUsers(internalGroup, () => configUser.Value.HasInternalProxyAccess);
            AddAndUpdateUsers(metricsGroup, () => configUser.Value.HasMetricsAccess);
            void AddAndUpdateUsers(AutherConfig group, Func<bool> hasAccess)
            {
                var groupUser = group.Auths!.FirstOrDefault(u => string.Equals(u.Username, auth.Username));
                if (groupUser == null && hasAccess())
                {
                    Log.Debug($"Add user `{auth.Username}` to `{group.Name}`");
                    group.Auths!.Add(auth);
                    changed = true;
                    return;
                }

                if (!hasAccess() || groupUser == null || groupUser == auth) return;
                Log.Debug($"Update user `{auth.Username}` in `{group.Name}`");
                groupUser.Password = auth.Password;
                groupUser.File = null;
                changed = true;
            }
        }

        // Remove users that lack auther group role or do not exist anymore
        RemoveUsers(mullvadGroup, u => u.HasMullvadProxyAccess);
        RemoveUsers(internalGroup, u => u.HasInternalProxyAccess);
        RemoveUsers(metricsGroup, u => u.HasMetricsAccess);
        void RemoveUsers(AutherConfig group, Func<User, bool> hasAccess)
        {
            foreach (var groupAuth in group.Auths!.ToArray())
            {
                if (!string.IsNullOrWhiteSpace(groupAuth.Username) &&
                    gatewayConfig.Users.TryGetValue(groupAuth.Username, out var cfgUser) &&
                    hasAccess(cfgUser))
                    continue;

                Log.Debug($"Removing user `{groupAuth.Username}` from `{group.Name}`");
                changed |= group.Auths!.Remove(groupAuth);
            }
        }

        return Task.FromResult(changed);
    }
}
