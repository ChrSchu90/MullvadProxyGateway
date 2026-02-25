namespace GostGen.Tests;

using GostGen.DTO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Test for <see cref="GostUserSync"/>
/// </summary>

[TestClass]
public class GostUserSyncTests
{
    [TestMethod]
    public async Task NoChangesAreMade()
    {
        var gatewayConfig = new GatewayConfig
        {
            Users = new Dictionary<string, User>
            {
                ["User1"] = new() { Password = "Password1", HasMullvadProxyAccess = true, HasInternalProxyAccess = true, HasMetricsAccess = true }
            }
        };

        var gostConfig = new GostConfig
        {
            Authers = [new AutherConfig { Name = GostUserSync.AutherMullvadGroup, Auths = [new() { Username = "User1", Password = "Password1"}] },
                       new AutherConfig { Name = GostUserSync.AutherInternalGroup, Auths = [new() { Username = "User1", Password = "Password1"}] },
                       new AutherConfig { Name = GostUserSync.AutherMetricsGroup, Auths = [new() { Username = "User1", Password = "Password1"}] }]
        };

        var result = await GostUserSync.UpdateAsync(gostConfig, gatewayConfig);
        Assert.IsFalse(result, "Change for same configuration was detected");
    }

    [TestMethod]
    public async Task UserAdded()
    {
        var gatewayConfig = new GatewayConfig { Users = new Dictionary<string, User> { ["User1"] = new() { Password = "Password1", HasMullvadProxyAccess = true, HasInternalProxyAccess = true, HasMetricsAccess = true } } };
        var gostConfig = new GostConfig();
        var result = await GostUserSync.UpdateAsync(gostConfig, gatewayConfig);
        Assert.IsTrue(result, "Change was not detected");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherMullvadGroup).Auths!.Any(a => a.Username == "User1"), "New user was not added inside auther group");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherInternalGroup).Auths!.Any(a => a.Username == "User1"), "New user was not added inside auther group");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherMetricsGroup).Auths!.Any(a => a.Username == "User1"), "New user was not added inside auther group");

        gostConfig = new GostConfig();
        gatewayConfig = new GatewayConfig { Users = new Dictionary<string, User> { ["User1"] = new() { Password = "Password1", HasMullvadProxyAccess = false, HasInternalProxyAccess = true, HasMetricsAccess = true } } };
        result = await GostUserSync.UpdateAsync(gostConfig, gatewayConfig);
        Assert.IsTrue(result, "Change was not detected");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherMullvadGroup).Auths!.All(a => a.Username != "User1"), "New was added to role mullvad without having it configured");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherInternalGroup).Auths!.Any(a => a.Username == "User1"), "New user was not added inside auther group");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherMetricsGroup).Auths!.Any(a => a.Username == "User1"), "New user was not added inside auther group");

        gostConfig = new GostConfig();
        gatewayConfig = new GatewayConfig { Users = new Dictionary<string, User> { ["User1"] = new() { Password = "Password1", HasMullvadProxyAccess = true, HasInternalProxyAccess = false, HasMetricsAccess = true } } };
        result = await GostUserSync.UpdateAsync(gostConfig, gatewayConfig);
        Assert.IsTrue(result, "Change was not detected");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherMullvadGroup).Auths!.Any(a => a.Username == "User1"), "New user was not added inside auther group");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherInternalGroup).Auths!.All(a => a.Username != "User1"), "New was added to role internal without having it configured");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherMetricsGroup).Auths!.Any(a => a.Username == "User1"), "New user was not added inside auther group");
        
        gostConfig = new GostConfig();
        gatewayConfig = new GatewayConfig { Users = new Dictionary<string, User> { ["User1"] = new() { Password = "Password1", HasMullvadProxyAccess = true, HasInternalProxyAccess = true, HasMetricsAccess = false } } };
        result = await GostUserSync.UpdateAsync(gostConfig, gatewayConfig);
        Assert.IsTrue(result, "Change was not detected");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherMullvadGroup).Auths!.Any(a => a.Username == "User1"), "New user was not added inside auther group");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherInternalGroup).Auths!.Any(a => a.Username == "User1"), "New user was not added inside auther group");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherMetricsGroup).Auths!.All(a => a.Username != "User1"), "New was added to role metrics without having it configured");
    }

    [TestMethod]
    public async Task UserPasswordChanged()
    {
        var gatewayConfig = new GatewayConfig
        {
            Users = new Dictionary<string, User>
            {
                ["User1"] = new() { Password = "Password123", HasMullvadProxyAccess = true, HasInternalProxyAccess = true, HasMetricsAccess = true }
            }
        };

        var gostConfig = new GostConfig
        {
            Authers = [new AutherConfig { Name = GostUserSync.AutherMullvadGroup, Auths = [new() { Username = "User1", Password = "Password1"}] },
                       new AutherConfig { Name = GostUserSync.AutherInternalGroup, Auths = [new() { Username = "User1", Password = "Password1"}] },
                       new AutherConfig { Name = GostUserSync.AutherMetricsGroup, Auths = [new() { Username = "User1", Password = "Password1"}] }]
        };

        var result = await GostUserSync.UpdateAsync(gostConfig, gatewayConfig);
        Assert.IsTrue(result, "Change was not detected");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherMullvadGroup).Auths!.Any(a => a.Password == "Password123"), "New user password was not changed inside auther group");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherInternalGroup).Auths!.Any(a => a.Password == "Password123"), "New user password was not changed inside auther group");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherMetricsGroup).Auths!.Any(a => a.Password == "Password123"), "New user password was not changed inside auther group");
    }

    [TestMethod]
    public async Task UserRemoved()
    {
        var gatewayConfig = new GatewayConfig
        {
            Users = new Dictionary<string, User>
            {
                //["User1"] = new User { Password = "Password1", HasMullvadProxyAccess = true, HasInternalProxyAccess = true, HasMetricsAccess = true }
            }
        };

        var gostConfig = new GostConfig
        {
            Authers = [new AutherConfig { Name = GostUserSync.AutherMullvadGroup, Auths = [new() { Username = "User1", Password = "Password1"}] },
                       new AutherConfig { Name = GostUserSync.AutherInternalGroup, Auths = [new() { Username = "User1", Password = "Password1"}] },
                       new AutherConfig { Name = GostUserSync.AutherMetricsGroup, Auths = [new() { Username = "User1", Password = "Password1"}] }]
        };

        var result = await GostUserSync.UpdateAsync(gostConfig, gatewayConfig);
        Assert.IsTrue(result, "Change was not detected");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherMullvadGroup).Auths!.All(a => a.Username != "User1"), "New user was not removed from auther group");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherInternalGroup).Auths!.All(a => a.Username != "User1"), "New user was not removed from auther group");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherMetricsGroup).Auths!.All(a => a.Username != "User1"), "New user was not removed from auther group");
    }

    [TestMethod]
    public async Task UserRemovedRoleMetrics()
    {
        var gatewayConfig = new GatewayConfig
        {
            Users = new Dictionary<string, User>
            {
                ["User1"] = new() { Password = "Password1", HasMullvadProxyAccess = true, HasInternalProxyAccess = true, HasMetricsAccess = false }
            }
        };

        var gostConfig = new GostConfig
        {
            Authers = [new AutherConfig { Name = GostUserSync.AutherMullvadGroup, Auths = [new() { Username = "User1", Password = "Password1"}] },
                       new AutherConfig { Name = GostUserSync.AutherInternalGroup, Auths = [new() { Username = "User1", Password = "Password1"}] },
                       new AutherConfig { Name = GostUserSync.AutherMetricsGroup, Auths = [new() { Username = "User1", Password = "Password1"}] }]
        };

        var result = await GostUserSync.UpdateAsync(gostConfig, gatewayConfig);
        Assert.IsTrue(result, "Change was not detected");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherMullvadGroup).Auths!.Any(a => a.Username == "User1"), "New user was removed from auther group");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherInternalGroup).Auths!.Any(a => a.Username == "User1"), "New user was removed from auther group");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherMetricsGroup).Auths!.All(a => a.Username != "User1"), "New user was not removed from auther group");
    }

    [TestMethod]
    public async Task UserRemovedRoleInternalPrxoy()
    {
        var gatewayConfig = new GatewayConfig
        {
            Users = new Dictionary<string, User>
            {
                ["User1"] = new() { Password = "Password1", HasMullvadProxyAccess = true, HasInternalProxyAccess = false, HasMetricsAccess = true }
            }
        };

        var gostConfig = new GostConfig
        {
            Authers = [new AutherConfig { Name = GostUserSync.AutherMullvadGroup, Auths = [new() { Username = "User1", Password = "Password1"}] },
             new AutherConfig { Name = GostUserSync.AutherInternalGroup, Auths = [new() { Username = "User1", Password = "Password1"}] },
             new AutherConfig { Name = GostUserSync.AutherMetricsGroup, Auths = [new() { Username = "User1", Password = "Password1"}] }]
        };

        var result = await GostUserSync.UpdateAsync(gostConfig, gatewayConfig);
        Assert.IsTrue(result, "Change was not detected");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherMullvadGroup).Auths!.Any(a => a.Username == "User1"), "New user was removed from auther group");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherMetricsGroup).Auths!.Any(a => a.Username == "User1"), "New user was removed from auther group");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherInternalGroup).Auths!.All(a => a.Username != "User1"), "New user was not removed from auther group");
    }

    [TestMethod]
    public async Task UserRemovedRoleMullvadPrxoy()
    {
        var gatewayConfig = new GatewayConfig
        {
            Users = new Dictionary<string, User>
            {
                ["User1"] = new() { Password = "Password1", HasMullvadProxyAccess = false, HasInternalProxyAccess = true, HasMetricsAccess = true }
            }
        };

        var gostConfig = new GostConfig
        {
            Authers = [new AutherConfig { Name = GostUserSync.AutherMullvadGroup, Auths = [new() { Username = "User1", Password = "Password1"}] },
                       new AutherConfig { Name = GostUserSync.AutherInternalGroup, Auths = [new() { Username = "User1", Password = "Password1"}] },
                       new AutherConfig { Name = GostUserSync.AutherMetricsGroup, Auths = [new() { Username = "User1", Password = "Password1"}] }]
        };

        var result = await GostUserSync.UpdateAsync(gostConfig, gatewayConfig);
        Assert.IsTrue(result, "Change was not detected");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherMetricsGroup).Auths!.Any(a => a.Username == "User1"), "New user was removed from auther group");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherInternalGroup).Auths!.Any(a => a.Username == "User1"), "New user was removed from auther group");
        Assert.IsTrue(gostConfig.Authers!.First(g => g.Name == GostUserSync.AutherMullvadGroup).Auths!.All(a => a.Username != "User1"), "New user was not removed from auther group");
    }
}