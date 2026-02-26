namespace GostGen.Tests;

using GostGen.DTO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Test for <see cref="GostProxySync"/>
/// </summary>
[TestClass]
public class GostProxySyncTests
{
    /// <summary>
    /// Tests for <see cref="GostProxySync.GetMullvadRelaysAsync"/> from API
    /// </summary>
    [TestMethod]
    public async Task GetMullvadRelaysFromApiAsync()
    {
        if (!await IsRelaysApiReachable())
            Assert.Inconclusive("API is not reachable, skipping test.");

        if (File.Exists(GostProxySync.RelayFile)) File.Delete(GostProxySync.RelayFile);
        var relays = await GostProxySync.GetMullvadRelaysAsync().ConfigureAwait(false);
        Assert.IsNotNull(relays, $"Failed to get relays from Mullvad API {GostProxySync.RelayApiUrl}");
        Assert.IsTrue(relays.Any(), $"API {GostProxySync.RelayApiUrl} retuned empty list.");
    }

    /// <summary>
    /// Tests for <see cref="GostProxySync.GetMullvadRelaysAsync"/> from file
    /// </summary>
    [TestMethod]
    public async Task GetMullvadRelaysFromFileAsync()
    {
        if (File.Exists(GostProxySync.RelayFile)) File.Delete(GostProxySync.RelayFile);
        var relayJson = await GetEmbeddedRelayJsonAsync().ConfigureAwait(false);
        Assert.IsFalse(string.IsNullOrWhiteSpace(relayJson), "Failed to load test json from resources");

        await File.WriteAllTextAsync(GostProxySync.RelayFile, relayJson).ConfigureAwait(false);
        Assert.IsTrue(File.Exists(GostProxySync.RelayFile), "Failed to save test relay json as file");

        var relays = await GostProxySync.GetMullvadRelaysAsync().ConfigureAwait(false);
        Assert.IsNotNull(relays, $"Failed to get relays from test file `{GostProxySync.RelayFile}`");
        Assert.AreEqual(582, relays.Count, "Amount of relays does not match with the test file");
    }

    /// <summary>
    /// Tests for <see cref="GostProxySync.ApplyProxyFilter"/>
    /// </summary>
    [TestMethod]
    public async Task ApplyProxyFilter()
    {
        var relays = (await GetMullvadRelaysAsync().ConfigureAwait(false))?.ToList();
        Assert.IsNotNull(relays, "Failed to get test relays");
        Assert.AreEqual(582, relays.Count, "Test relays amount does not match");

        var config = new GatewayConfig { ProxyFilter = new() };

        // Relay with null info
        var nullRelay = new MullvadRelay();
        relays.Add(nullRelay);
        var filtered = GostProxySync.ApplyProxyFilter(relays, config);
        Assert.AreEqual(relays.Count - 1, filtered.Count, "Relay with NULL data has not been filtered");
        Assert.IsTrue(relays.Remove(nullRelay), "Failed to remove NULL data relay from test data");

        // No filter
        filtered = GostProxySync.ApplyProxyFilter(relays, config);
        Assert.AreEqual(relays.Count, filtered.Count, "Missing relays with empty filter configuration");

        // Only owned servers
        config = new GatewayConfig { ProxyFilter = new() { OwnedOnly = true } };
        filtered = GostProxySync.ApplyProxyFilter(relays, config);
        var expectedCnt = relays.Count(r => r.Owned);
        Assert.AreEqual(expectedCnt, filtered.Count, "Filter result for owned does not match");

        // County Include filter
        config = new GatewayConfig { ProxyFilter = new() { Country = { Include = ["DE", "switzerland"] } } };
        filtered = GostProxySync.ApplyProxyFilter(relays, config);
        expectedCnt = relays.Count(r => string.Equals(r.CountryCode, "de", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(r.CountryName, "Switzerland", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(expectedCnt, filtered.Count, "Filter result for county include does not match");

        // County Exclude filter
        config = new GatewayConfig { ProxyFilter = new() { Country = { Exclude = ["DE", "switzerland"] } } };
        filtered = GostProxySync.ApplyProxyFilter(relays, config);
        expectedCnt = relays.Count(r => !string.Equals(r.CountryCode, "de", StringComparison.OrdinalIgnoreCase) &&
                                        !string.Equals(r.CountryName, "Switzerland", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(expectedCnt, filtered.Count, "Filter result for county include does not match");

        // City Include filter
        config = new GatewayConfig { ProxyFilter = new() { City = { Include = ["FRA", "zurich"] } } };
        filtered = GostProxySync.ApplyProxyFilter(relays, config);
        expectedCnt = relays.Count(r => string.Equals(r.CityCode, "fra", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(r.CityName, "Zurich", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(expectedCnt, filtered.Count, "Filter result for county include does not match");

        // City Exclude filter
        config = new GatewayConfig { ProxyFilter = new() { City = { Exclude = ["FRA", "zurich"] } } };
        filtered = GostProxySync.ApplyProxyFilter(relays, config);
        expectedCnt = relays.Count(r => !string.Equals(r.CityCode, "fra", StringComparison.OrdinalIgnoreCase) &&
                                        !string.Equals(r.CityName, "Zurich", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(expectedCnt, filtered.Count, "Filter result for county include does not match");
    }

    /// <summary>
    /// Tests for <see cref="GostProxySync.FindPoolAreaPort"/>
    /// </summary>
    [TestMethod]
    public void FindPoolAreaPort()
    {
        for (byte maxServerPerCity = 1; maxServerPerCity <= 50; maxServerPerCity++)
        {
            // New pool
            var gostConfig = new GostConfig();
            var gatewayConfig = new GatewayConfig { MaxServersPerCity = maxServerPerCity };

            var port = GostProxySync.FindPoolAreaPort(gostConfig, gatewayConfig, "al", "tia");
            var expectedPort = GostProxySync.ProxyPortStart;
            Assert.AreEqual(expectedPort, port, "Empty config should result in area start port");

            // Add pool
            gostConfig = new GostConfig { Services = [new ServiceConfig { Name = "service-al-tia-1", Addr = $":{GostProxySync.ProxyPortStart}" }] };
            port = GostProxySync.FindPoolAreaPort(gostConfig, gatewayConfig, "ar", "bue");
            expectedPort = GostProxySync.ProxyPortStart + maxServerPerCity;
            Assert.AreEqual(expectedPort, port, "New pool has not been created after existing pool");

            // Reuse same pool
            gostConfig = new GostConfig
            {
                Services = [
                               new ServiceConfig { Name = "service-al-tia-1", Addr = $":{GostProxySync.ProxyPortStart}"},
                               new ServiceConfig { Name = "service-ar-bue-1", Addr = $":{GostProxySync.ProxyPortStart + 2 * maxServerPerCity}"}
                           ]
            };
            port = GostProxySync.FindPoolAreaPort(gostConfig, gatewayConfig, "al", "tia");
            expectedPort = GostProxySync.ProxyPortStart;
            Assert.AreEqual(expectedPort, port, "Same pool does not return previous port");


            // Add pool at end with gap
            gostConfig = new GostConfig
            {
                Services = [
                               new ServiceConfig { Name = "service-al-tia-1", Addr = $":{GostProxySync.ProxyPortStart}"},
                               new ServiceConfig { Name = "service-ar-bue-1", Addr = $":{GostProxySync.ProxyPortStart + 4 * maxServerPerCity}"}
                           ]
            };
            port = GostProxySync.FindPoolAreaPort(gostConfig, gatewayConfig, "au", "adl");
            expectedPort = GostProxySync.ProxyPortStart + 4 * maxServerPerCity + maxServerPerCity;
            Assert.AreEqual(expectedPort, port, "Pool was not added at the very end");

            // Out of area
            gostConfig = new GostConfig
            {
                Services = [
                               new ServiceConfig { Name = "service-al-tia-1", Addr = $":{GostProxySync.ProxyPortStart}"},
                               new ServiceConfig { Name = "service-ar-bue-1", Addr = $":{GostProxySync.ProxyPortEnd - maxServerPerCity}"}
                           ]
            };
            port = GostProxySync.FindPoolAreaPort(gostConfig, gatewayConfig, "au", "adl");
            expectedPort = -1;
            Assert.AreEqual(expectedPort, port, "Pool was added outside of area");
        }
    }

    /// <summary>
    /// Tests for <see cref="GostProxySync.ExportProxyCsv"/>
    /// </summary>
    [TestMethod]
    public void ExportProxyCsv()
    {
        // Invalid data to check exception is caught
        GostProxySync.ExportProxyCsv(null!);

        var proxies = new List<GostProxySync.Proxy>
                          {
                              new(232,
                              new MullvadRelay {
                                                    CountryName = "Germany",
                                                    CountryCode = "de",
                                                    CityName = "Frankfurt",
                                                    CityCode = "fra",
                                                    SocksName = "test.mullvad.net"
                                                },
                              new ServiceConfig { Addr = ":5555"},
                              new ChainConfig(),
                              new HopConfig())
                          };

        if (File.Exists(GostProxySync.ExportCsvFile)) File.Delete(GostProxySync.ExportCsvFile);
        GostProxySync.ExportProxyCsv(proxies);
        Assert.IsTrue(File.Exists(GostProxySync.ExportCsvFile), "Export .csv hasn't been created");

        var csvContent = File.ReadAllText(GostProxySync.ExportCsvFile);
        Assert.IsFalse(string.IsNullOrWhiteSpace(csvContent), "Export .csv has no content");

        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(2, lines.Length, "CSV should contain header + 1 data row");

        var headers = lines[0].Split(',');
        Assert.AreEqual(8, headers.Length);
        Assert.AreEqual("Country", headers[0]);
        Assert.AreEqual("Country Code", headers[1]);
        Assert.AreEqual("City", headers[2]);
        Assert.AreEqual("City Code", headers[3]);
        Assert.AreEqual("City No.", headers[4]);
        Assert.AreEqual("Location Code", headers[5]);
        Assert.AreEqual("Port", headers[6]);
        Assert.AreEqual("Target", headers[7]);

        var values = lines[1].Split(',');
        Assert.AreEqual(headers.Length, values.Length);
        Assert.AreEqual("\"Germany\"", values[0]);
        Assert.AreEqual("de", values[1]);
        Assert.AreEqual("\"Frankfurt\"", values[2]);
        Assert.AreEqual("fra", values[3]);
        Assert.AreEqual("232", values[4]);
        Assert.AreEqual("de-fra", values[5]);
        Assert.AreEqual("5555", values[6]);
        Assert.AreEqual("test.mullvad.net", values[7]);
    }

    /// <summary>
    /// Tests for <see cref="GostProxySync.ExportProxyJson"/>
    /// </summary>
    [TestMethod]
    public void ExportProxyJson()
    {
        // Invalid data to check exception is caught
        GostProxySync.ExportProxyJson(null!);

        var proxies = new List<GostProxySync.Proxy>
                          {
                              new(232,
                              new MullvadRelay {
                                                   CountryName = "Germany",
                                                   CountryCode = "de",
                                                   CityName = "Frankfurt",
                                                   CityCode = "fra",
                                                   SocksName = "test.mullvad.net"
                                               },
                              new ServiceConfig { Addr = ":5555"},
                              new ChainConfig(),
                              new HopConfig())
                          };

        if (File.Exists(GostProxySync.ExportJsonFile)) File.Delete(GostProxySync.ExportJsonFile);
        GostProxySync.ExportProxyJson(proxies);
        Assert.IsTrue(File.Exists(GostProxySync.ExportJsonFile), "Export .json hasn't been created");

        var json = File.ReadAllText(GostProxySync.ExportJsonFile);
        Assert.IsFalse(string.IsNullOrWhiteSpace(json), "Export .json has no content");
        using var doc = JsonDocument.Parse(json);
        var first = doc.RootElement[0];

        Assert.AreEqual("Germany", first.GetProperty("Country").GetString());
        Assert.AreEqual("de", first.GetProperty("CountryCode").GetString());
        Assert.AreEqual("Frankfurt", first.GetProperty("City").GetString());
        Assert.AreEqual("fra", first.GetProperty("CityCode").GetString());
        Assert.AreEqual(232, first.GetProperty("CityNo").GetInt32());
        Assert.AreEqual("de-fra", first.GetProperty("LocationCode").GetString());
        Assert.AreEqual(5555, first.GetProperty("Port").GetInt32());
        Assert.AreEqual("test.mullvad.net", first.GetProperty("Target").GetString());
    }

    /// <summary>
    /// Tests for <see cref="GostProxySync.UpdateLocalProxyAsync"/>
    /// </summary>
    [TestMethod]
    public async Task UpdateLocalProxyAsync()
    {
        var gatewayCfg = new GatewayConfig();
        var gostCfg = new GostConfig();

        var changed = await GostProxySync.UpdateLocalProxyAsync(gostCfg, gatewayCfg, "eth0").ConfigureAwait(false);
        Assert.IsTrue(changed, "Local proxy hasn't been generated");

        changed = await GostProxySync.UpdateLocalProxyAsync(gostCfg, gatewayCfg, "eth0").ConfigureAwait(false);
        Assert.IsFalse(changed, "Local proxy hab been updated without change");

        changed = await GostProxySync.UpdateLocalProxyAsync(gostCfg, gatewayCfg, "ens0").ConfigureAwait(false);
        Assert.IsTrue(changed, "Local proxy hasn't been updated, but network interface has changed.");
    }

    /// <summary>
    /// Tests for content changes of <see cref="GostProxySync.UpdateMullvadServersAsync"/>
    /// </summary>
    [TestMethod]
    public async Task UpdateMullvadServersAsyncContent()
    {
        if (File.Exists(GostProxySync.RelayFile)) File.Delete(GostProxySync.RelayFile);
        var relayJson = (await GetMullvadRelaysAsync().ConfigureAwait(false))?.ToList();
        Assert.IsNotNull(relayJson, "Failed to load test json from resources");
        await File.WriteAllTextAsync(GostProxySync.RelayFile, JsonSerializer.Serialize(relayJson)).ConfigureAwait(false);
        Assert.IsTrue(new FileInfo(GostProxySync.RelayFile).Length > 100, "Failed to save test filled relay json as file");

        var gatewayPoolCfg = new GatewayConfig { UpdateServersOnStartup = true, CityRandomPools = true, MaxServersPerCity = 10 };
        var gatewayWithoutPoolCfg = new GatewayConfig { UpdateServersOnStartup = true, CityRandomPools = false, MaxServersPerCity = 10 };
        var gostCfg = new GostConfig();
        var changed = await GostProxySync.UpdateMullvadServersAsync(gostCfg, gatewayPoolCfg, "eth0").ConfigureAwait(false);
        Assert.IsTrue(changed, "Proxies must be changed if GOST config was empty");
        var contentWithPool = GostConfig.ToYaml(gostCfg);
        changed = await GostProxySync.UpdateMullvadServersAsync(gostCfg, gatewayWithoutPoolCfg, "eth0").ConfigureAwait(false);
        Assert.IsTrue(changed, "Proxies must be changed if GOST config with pool rebuild without pool");
        changed = await GostProxySync.UpdateMullvadServersAsync(gostCfg, gatewayPoolCfg, "eth0").ConfigureAwait(false);
        Assert.IsTrue(changed, "Proxies must be changed if GOST config without pool rebuild with pool");
        var contentWithPool2 = GostConfig.ToYaml(gostCfg);
        Assert.AreEqual(contentWithPool, contentWithPool2, "Content does not match when changing from with pool to without pool");

        gostCfg = new GostConfig();
        changed = await GostProxySync.UpdateMullvadServersAsync(gostCfg, gatewayWithoutPoolCfg, "eth0").ConfigureAwait(false);
        Assert.IsTrue(changed, "Proxies must be changed if GOST config was empty");
        var contentWithoutPool = GostConfig.ToYaml(gostCfg);
        changed = await GostProxySync.UpdateMullvadServersAsync(gostCfg, gatewayPoolCfg, "eth0").ConfigureAwait(false);
        Assert.IsTrue(changed, "Proxies must be changed if GOST config without pool rebuild with pool");
        changed = await GostProxySync.UpdateMullvadServersAsync(gostCfg, gatewayWithoutPoolCfg, "eth0").ConfigureAwait(false);
        Assert.IsTrue(changed, "Proxies must be changed if GOST config with pool rebuild without pool");
        var contentWithoutPool2 = GostConfig.ToYaml(gostCfg);
        Assert.AreEqual(contentWithoutPool, contentWithoutPool2, "Content does not match when changing from without pool to with pool");

        var gatewayCfg = new GatewayConfig { UpdateServersOnStartup = false, CityRandomPools = true, MaxServersPerCity = 10 };
        gostCfg = new GostConfig();
        changed = await GostProxySync.UpdateMullvadServersAsync(gostCfg, gatewayCfg, "eth0").ConfigureAwait(false);
        Assert.IsTrue(changed, "Proxies must be changed if GOST config was empty");
        var content1 = GostConfig.ToYaml(gostCfg);
        gatewayCfg.UpdateServersOnStartup = true;
        changed = await GostProxySync.UpdateMullvadServersAsync(gostCfg, gatewayCfg, "eth0").ConfigureAwait(false);
        Assert.IsFalse(changed, "Proxies should not been changed since relays did not changed");
        var content2 = GostConfig.ToYaml(gostCfg);
        Assert.AreEqual(content1, content2, "Proxies should not been changed since relays did not changed");

        var testRelays = new List<MullvadRelay>
                             {
                                 new() { CountryName  = "CountryA", CountryCode = "coa", CityName = "CityA", CityCode = "cia", Hostname = "coa-cia-1", SocksName ="socks-coa-cia-1", SocksPort = 123},
                                 new() { CountryName  = "CountryA", CountryCode = "coa", CityName = "CityA", CityCode = "cia", Hostname = "coa-cia-2", SocksName ="socks-coa-cia-2", SocksPort = 123},
                                 new() { CountryName  = "CountryB", CountryCode = "cob", CityName = "CityB", CityCode = "cib", Hostname = "cob-cib-1", SocksName ="socks-cob-cib-1", SocksPort = 123},
                                 new() { CountryName  = "CountryB", CountryCode = "cob", CityName = "CityB", CityCode = "cib", Hostname = "cob-cib-2", SocksName ="socks-cob-cib-2", SocksPort = 123},
                                 new() { CountryName  = "CountryB", CountryCode = "cob", CityName = "CityB", CityCode = "cib", Hostname = "cob-cib-3", SocksName ="socks-cob-cib-3", SocksPort = 123}
                             };
        await File.WriteAllTextAsync(GostProxySync.RelayFile, JsonSerializer.Serialize(testRelays)).ConfigureAwait(false);
        Assert.IsTrue(new FileInfo(GostProxySync.RelayFile).Length > 100, "Failed to save test filled relay json as file");

        gatewayCfg = new GatewayConfig { UpdateServersOnStartup = true, CityRandomPools = true, MaxServersPerCity = 10 };
        gostCfg = new GostConfig();
        changed = await GostProxySync.UpdateMullvadServersAsync(gostCfg, gatewayCfg, "eth0").ConfigureAwait(false);
        Assert.IsTrue(changed, "Proxies must be changed if GOST config was empty");
        Assert.IsNotNull(gostCfg.Services);
        Assert.IsNotNull(gostCfg.Chains);
        Assert.AreEqual(7, gostCfg.Services.Count, "Unexpected amount of services");
        Assert.AreEqual(7, gostCfg.Chains.Count, "Unexpected amount of chains");
        Assert.AreEqual(gostCfg.Services.Count, gostCfg.Chains.Count, "Amount of chains and services must be the same");

        Assert.AreEqual("service-coa-cia-pool", gostCfg.Services[0].Name);
        Assert.AreEqual("eth0", gostCfg.Services[0].Interface);
        Assert.AreEqual(":2000", gostCfg.Services[0].Addr);
        Assert.AreEqual(GostProxySync.NetworkProtocol, gostCfg.Services[0].Listener?.Type);
        Assert.AreEqual(GostProxySync.SocksType, gostCfg.Services[0].Handler?.Type);
        Assert.AreEqual(GostUserSync.AutherMullvadGroup, gostCfg.Services[0].Handler?.Auther);
        Assert.AreEqual("chain-coa-cia-pool", gostCfg.Services[0].Handler?.Chain);
        Assert.AreEqual("service-coa-cia-1", gostCfg.Services[1].Name);
        Assert.AreEqual("eth0", gostCfg.Services[1].Interface);
        Assert.AreEqual(":2001", gostCfg.Services[1].Addr);
        Assert.AreEqual(GostProxySync.NetworkProtocol, gostCfg.Services[1].Listener?.Type);
        Assert.AreEqual(GostProxySync.SocksType, gostCfg.Services[1].Handler?.Type);
        Assert.AreEqual(GostUserSync.AutherMullvadGroup, gostCfg.Services[1].Handler?.Auther);
        Assert.AreEqual("chain-coa-cia-1", gostCfg.Services[1].Handler?.Chain);
        Assert.AreEqual("service-coa-cia-2", gostCfg.Services[2].Name);
        Assert.AreEqual("eth0", gostCfg.Services[2].Interface);
        Assert.AreEqual(":2002", gostCfg.Services[2].Addr);
        Assert.AreEqual(GostProxySync.NetworkProtocol, gostCfg.Services[2].Listener?.Type);
        Assert.AreEqual(GostProxySync.SocksType, gostCfg.Services[2].Handler?.Type);
        Assert.AreEqual(GostUserSync.AutherMullvadGroup, gostCfg.Services[2].Handler?.Auther);
        Assert.AreEqual("chain-coa-cia-2", gostCfg.Services[2].Handler?.Chain);
        Assert.AreEqual("service-cob-cib-pool", gostCfg.Services[3].Name);
        Assert.AreEqual("eth0", gostCfg.Services[3].Interface);
        Assert.AreEqual(":2010", gostCfg.Services[3].Addr);
        Assert.AreEqual(GostProxySync.NetworkProtocol, gostCfg.Services[3].Listener?.Type);
        Assert.AreEqual(GostProxySync.SocksType, gostCfg.Services[3].Handler?.Type);
        Assert.AreEqual(GostUserSync.AutherMullvadGroup, gostCfg.Services[3].Handler?.Auther);
        Assert.AreEqual("chain-cob-cib-pool", gostCfg.Services[3].Handler?.Chain);
        Assert.AreEqual("service-cob-cib-1", gostCfg.Services[4].Name);
        Assert.AreEqual("eth0", gostCfg.Services[4].Interface);
        Assert.AreEqual(":2011", gostCfg.Services[4].Addr);
        Assert.AreEqual(GostProxySync.NetworkProtocol, gostCfg.Services[4].Listener?.Type);
        Assert.AreEqual(GostProxySync.SocksType, gostCfg.Services[4].Handler?.Type);
        Assert.AreEqual(GostUserSync.AutherMullvadGroup, gostCfg.Services[4].Handler?.Auther);
        Assert.AreEqual("chain-cob-cib-1", gostCfg.Services[4].Handler?.Chain);
        Assert.AreEqual("service-cob-cib-2", gostCfg.Services[5].Name);
        Assert.AreEqual("eth0", gostCfg.Services[5].Interface);
        Assert.AreEqual(":2012", gostCfg.Services[5].Addr);
        Assert.AreEqual(GostProxySync.NetworkProtocol, gostCfg.Services[5].Listener?.Type);
        Assert.AreEqual(GostProxySync.SocksType, gostCfg.Services[5].Handler?.Type);
        Assert.AreEqual(GostUserSync.AutherMullvadGroup, gostCfg.Services[5].Handler?.Auther);
        Assert.AreEqual("chain-cob-cib-2", gostCfg.Services[5].Handler?.Chain);
        Assert.AreEqual("service-cob-cib-3", gostCfg.Services[6].Name);
        Assert.AreEqual("eth0", gostCfg.Services[6].Interface);
        Assert.AreEqual(":2013", gostCfg.Services[6].Addr);
        Assert.AreEqual(GostProxySync.NetworkProtocol, gostCfg.Services[6].Listener?.Type);
        Assert.AreEqual(GostProxySync.SocksType, gostCfg.Services[6].Handler?.Type);
        Assert.AreEqual(GostUserSync.AutherMullvadGroup, gostCfg.Services[6].Handler?.Auther);
        Assert.AreEqual("chain-cob-cib-3", gostCfg.Services[6].Handler?.Chain);

        Assert.AreEqual("chain-coa-cia-1", gostCfg.Chains[0].Name);
        Assert.AreEqual(1, gostCfg.Chains[0].Hops?.Count);
        Assert.IsNull(gostCfg.Chains[0].Hops?[0].Selector);
        Assert.AreEqual(1, gostCfg.Chains[0].Hops?[0].Nodes?.Count);
        Assert.AreEqual("hop-chain-coa-cia-1", gostCfg.Chains[0].Hops?[0].Name);
        Assert.AreEqual("node-hop-chain-coa-cia-1", gostCfg.Chains[0].Hops?[0].Nodes?[0].Name);
        Assert.AreEqual(GostBypassSync.BypassMullvadGroup, gostCfg.Chains[0].Hops?[0].Nodes?[0].Bypass);
        Assert.AreEqual("socks-coa-cia-1:123", gostCfg.Chains[0].Hops?[0].Nodes?[0].Addr);
        Assert.AreEqual(GostProxySync.SocksType, gostCfg.Chains[0].Hops?[0].Nodes?[0].Connector?.Type);
        Assert.AreEqual(GostProxySync.NetworkProtocol, gostCfg.Chains[0].Hops?[0].Nodes?[0].Dialer?.Type);
        Assert.AreEqual("chain-coa-cia-2", gostCfg.Chains[1].Name);
        Assert.AreEqual(1, gostCfg.Chains[1].Hops?.Count);
        Assert.IsNull(gostCfg.Chains[1].Hops?[0].Selector);
        Assert.AreEqual(1, gostCfg.Chains[1].Hops?[0].Nodes?.Count);
        Assert.AreEqual("hop-chain-coa-cia-2", gostCfg.Chains[1].Hops?[0].Name);
        Assert.AreEqual("node-hop-chain-coa-cia-2", gostCfg.Chains[1].Hops?[0].Nodes?[0].Name);
        Assert.AreEqual(GostBypassSync.BypassMullvadGroup, gostCfg.Chains[1].Hops?[0].Nodes?[0].Bypass);
        Assert.AreEqual("socks-coa-cia-2:123", gostCfg.Chains[1].Hops?[0].Nodes?[0].Addr);
        Assert.AreEqual(GostProxySync.SocksType, gostCfg.Chains[0].Hops?[0].Nodes?[0].Connector?.Type);
        Assert.AreEqual(GostProxySync.NetworkProtocol, gostCfg.Chains[1].Hops?[0].Nodes?[0].Dialer?.Type);

        Assert.AreEqual("chain-coa-cia-pool", gostCfg.Chains[2].Name);
        Assert.AreEqual(1, gostCfg.Chains[2].Hops?.Count);
        Assert.IsNotNull(gostCfg.Chains[2].Hops?[0].Selector);
        Assert.AreEqual(2, gostCfg.Chains[2].Hops?[0].Nodes?.Count);
        Assert.AreEqual("hop-chain-coa-cia-pool", gostCfg.Chains[2].Hops?[0].Name);
        Assert.AreEqual("node-hop-chain-coa-cia-pool-1", gostCfg.Chains[2].Hops?[0].Nodes?[0].Name);
        Assert.AreEqual(GostBypassSync.BypassMullvadGroup, gostCfg.Chains[2].Hops?[0].Nodes?[0].Bypass);
        Assert.AreEqual("socks-coa-cia-1:123", gostCfg.Chains[2].Hops?[0].Nodes?[0].Addr);
        Assert.AreEqual(GostProxySync.SocksType, gostCfg.Chains[2].Hops?[0].Nodes?[0].Connector?.Type);
        Assert.AreEqual(GostProxySync.NetworkProtocol, gostCfg.Chains[2].Hops?[0].Nodes?[0].Dialer?.Type);
        Assert.AreEqual(GostBypassSync.BypassMullvadGroup, gostCfg.Chains[2].Hops?[0].Nodes?[1].Bypass);
        Assert.AreEqual("socks-coa-cia-2:123", gostCfg.Chains[2].Hops?[0].Nodes?[1].Addr);
        Assert.AreEqual(GostProxySync.SocksType, gostCfg.Chains[2].Hops?[0].Nodes?[1].Connector?.Type);
        Assert.AreEqual(GostProxySync.NetworkProtocol, gostCfg.Chains[2].Hops?[0].Nodes?[1].Dialer?.Type);



        Assert.AreEqual("chain-cob-cib-1", gostCfg.Chains[3].Name);
        Assert.AreEqual(1, gostCfg.Chains[3].Hops?.Count);
        Assert.IsNull(gostCfg.Chains[3].Hops?[0].Selector);
        Assert.AreEqual(1, gostCfg.Chains[3].Hops?[0].Nodes?.Count);
        Assert.AreEqual("hop-chain-cob-cib-1", gostCfg.Chains[3].Hops?[0].Name);
        Assert.AreEqual("node-hop-chain-cob-cib-1", gostCfg.Chains[3].Hops?[0].Nodes?[0].Name);
        Assert.AreEqual(GostBypassSync.BypassMullvadGroup, gostCfg.Chains[3].Hops?[0].Nodes?[0].Bypass);
        Assert.AreEqual("socks-cob-cib-1:123", gostCfg.Chains[3].Hops?[0].Nodes?[0].Addr);
        Assert.AreEqual(GostProxySync.SocksType, gostCfg.Chains[3].Hops?[0].Nodes?[0].Connector?.Type);
        Assert.AreEqual(GostProxySync.NetworkProtocol, gostCfg.Chains[3].Hops?[0].Nodes?[0].Dialer?.Type);
        Assert.AreEqual("chain-cob-cib-2", gostCfg.Chains[4].Name);
        Assert.AreEqual(1, gostCfg.Chains[4].Hops?.Count);
        Assert.IsNull(gostCfg.Chains[4].Hops?[0].Selector);
        Assert.AreEqual(1, gostCfg.Chains[4].Hops?[0].Nodes?.Count);
        Assert.AreEqual("hop-chain-cob-cib-2", gostCfg.Chains[4].Hops?[0].Name);
        Assert.AreEqual("node-hop-chain-cob-cib-2", gostCfg.Chains[4].Hops?[0].Nodes?[0].Name);
        Assert.AreEqual(GostBypassSync.BypassMullvadGroup, gostCfg.Chains[4].Hops?[0].Nodes?[0].Bypass);
        Assert.AreEqual("socks-cob-cib-2:123", gostCfg.Chains[4].Hops?[0].Nodes?[0].Addr);
        Assert.AreEqual(GostProxySync.SocksType, gostCfg.Chains[4].Hops?[0].Nodes?[0].Connector?.Type);
        Assert.AreEqual(GostProxySync.NetworkProtocol, gostCfg.Chains[4].Hops?[0].Nodes?[0].Dialer?.Type);
        Assert.AreEqual("chain-cob-cib-3", gostCfg.Chains[5].Name);
        Assert.AreEqual(1, gostCfg.Chains[5].Hops?.Count);
        Assert.IsNull(gostCfg.Chains[5].Hops?[0].Selector);
        Assert.AreEqual(1, gostCfg.Chains[5].Hops?[0].Nodes?.Count);
        Assert.AreEqual("hop-chain-cob-cib-3", gostCfg.Chains[5].Hops?[0].Name);
        Assert.AreEqual("node-hop-chain-cob-cib-3", gostCfg.Chains[5].Hops?[0].Nodes?[0].Name);
        Assert.AreEqual(GostBypassSync.BypassMullvadGroup, gostCfg.Chains[5].Hops?[0].Nodes?[0].Bypass);
        Assert.AreEqual("socks-cob-cib-3:123", gostCfg.Chains[5].Hops?[0].Nodes?[0].Addr);
        Assert.AreEqual(GostProxySync.SocksType, gostCfg.Chains[5].Hops?[0].Nodes?[0].Connector?.Type);
        Assert.AreEqual(GostProxySync.NetworkProtocol, gostCfg.Chains[5].Hops?[0].Nodes?[0].Dialer?.Type);

        Assert.AreEqual("chain-cob-cib-pool", gostCfg.Chains[6].Name);
        Assert.AreEqual(1, gostCfg.Chains[6].Hops?.Count);
        Assert.IsNotNull(gostCfg.Chains[6].Hops?[0].Selector);
        Assert.AreEqual(3, gostCfg.Chains[6].Hops?[0].Nodes?.Count);
        Assert.AreEqual("hop-chain-cob-cib-pool", gostCfg.Chains[6].Hops?[0].Name);
        Assert.AreEqual("node-hop-chain-cob-cib-pool-1", gostCfg.Chains[6].Hops?[0].Nodes?[0].Name);
        Assert.AreEqual(GostBypassSync.BypassMullvadGroup, gostCfg.Chains[6].Hops?[0].Nodes?[0].Bypass);
        Assert.AreEqual("socks-cob-cib-1:123", gostCfg.Chains[6].Hops?[0].Nodes?[0].Addr);
        Assert.AreEqual(GostProxySync.SocksType, gostCfg.Chains[6].Hops?[0].Nodes?[0].Connector?.Type);
        Assert.AreEqual(GostProxySync.NetworkProtocol, gostCfg.Chains[6].Hops?[0].Nodes?[0].Dialer?.Type);
        Assert.AreEqual(GostBypassSync.BypassMullvadGroup, gostCfg.Chains[6].Hops?[0].Nodes?[1].Bypass);
        Assert.AreEqual("socks-cob-cib-2:123", gostCfg.Chains[6].Hops?[0].Nodes?[1].Addr);
        Assert.AreEqual(GostProxySync.SocksType, gostCfg.Chains[6].Hops?[0].Nodes?[1].Connector?.Type);
        Assert.AreEqual(GostProxySync.NetworkProtocol, gostCfg.Chains[6].Hops?[0].Nodes?[1].Dialer?.Type);
        Assert.AreEqual(GostBypassSync.BypassMullvadGroup, gostCfg.Chains[6].Hops?[0].Nodes?[2].Bypass);
        Assert.AreEqual("socks-cob-cib-3:123", gostCfg.Chains[6].Hops?[0].Nodes?[2].Addr);
        Assert.AreEqual(GostProxySync.SocksType, gostCfg.Chains[6].Hops?[0].Nodes?[2].Connector?.Type);
        Assert.AreEqual(GostProxySync.NetworkProtocol, gostCfg.Chains[6].Hops?[0].Nodes?[2].Dialer?.Type);
    }

    /// <summary>
    /// Tests for change tracking of <see cref="GostProxySync.UpdateMullvadServersAsync"/>
    /// </summary>
    [TestMethod]
    public async Task UpdateMullvadServersAsyncChangeTracking()
    {
        var gatewayCfg = new GatewayConfig { UpdateServersOnStartup = false };
        var gostCfg = new GostConfig();

        if (File.Exists(GostProxySync.RelayFile)) File.Delete(GostProxySync.RelayFile);
        await File.WriteAllTextAsync(GostProxySync.RelayFile, "[]").ConfigureAwait(false);
        Assert.IsTrue(File.Exists(GostProxySync.RelayFile), "Failed to save empty test relay json as file");
        var changed = await GostProxySync.UpdateMullvadServersAsync(gostCfg, gatewayCfg, "eth0").ConfigureAwait(false);
        Assert.IsFalse(changed, "Proxies can't be changed if no relays are available");

        var relayJson = (await GetMullvadRelaysAsync().ConfigureAwait(false))?.ToList();
        Assert.IsNotNull(relayJson, "Failed to load test json from resources");
        await File.WriteAllTextAsync(GostProxySync.RelayFile, JsonSerializer.Serialize(relayJson)).ConfigureAwait(false);
        Assert.IsTrue(new FileInfo(GostProxySync.RelayFile).Length > 100, "Failed to save test filled relay json as file");
        changed = await GostProxySync.UpdateMullvadServersAsync(gostCfg, gatewayCfg, "eth0").ConfigureAwait(false);
        Assert.IsTrue(changed, "Proxies must be changed if GOST config was empty");

        changed = await GostProxySync.UpdateMullvadServersAsync(gostCfg, gatewayCfg, "eth0").ConfigureAwait(false);
        Assert.IsFalse(changed, "As long as UpdateServersOnStartup is false the proxies should not be updated if any exists");

        gatewayCfg.UpdateServersOnStartup = true;
        changed = await GostProxySync.UpdateMullvadServersAsync(gostCfg, gatewayCfg, "eth0").ConfigureAwait(false);
        Assert.IsFalse(changed, "As long as no new relay servers have been added the proxy list should not be changed");

        var removedRelay = relayJson.ElementAt(0);
        Assert.IsTrue(relayJson.Remove(removedRelay), "Failed to remove test relay");
        await File.WriteAllTextAsync(GostProxySync.RelayFile, JsonSerializer.Serialize(relayJson)).ConfigureAwait(false);
        changed = await GostProxySync.UpdateMullvadServersAsync(gostCfg, gatewayCfg, "eth0").ConfigureAwait(false);
        Assert.IsTrue(changed, "A removed relay server should result in a changed since the related pool is updated");

        relayJson.Insert(0, removedRelay);
        await File.WriteAllTextAsync(GostProxySync.RelayFile, JsonSerializer.Serialize(relayJson)).ConfigureAwait(false);
        changed = await GostProxySync.UpdateMullvadServersAsync(gostCfg, gatewayCfg, "eth0").ConfigureAwait(false);
        Assert.IsTrue(changed, "A added relay server should result in a changed since the related pool is updated");
        Assert.IsTrue(relayJson.Remove(removedRelay), "Failed to remove test relay");

        var modRelay = removedRelay with { SocksName = "test" + removedRelay.SocksName };
        relayJson.Insert(0, modRelay);
        await File.WriteAllTextAsync(GostProxySync.RelayFile, JsonSerializer.Serialize(relayJson)).ConfigureAwait(false);
        changed = await GostProxySync.UpdateMullvadServersAsync(gostCfg, gatewayCfg, "eth0").ConfigureAwait(false);
        Assert.IsTrue(changed, "A renamed changed server socks name should result in a changed since the related pool is updated");
        Assert.IsTrue(relayJson.Remove(modRelay), "Failed to remove mod relay");

        modRelay = removedRelay with { CountryCode = "test" + removedRelay.CountryCode };
        relayJson.Insert(0, modRelay);
        await File.WriteAllTextAsync(GostProxySync.RelayFile, JsonSerializer.Serialize(relayJson)).ConfigureAwait(false);
        changed = await GostProxySync.UpdateMullvadServersAsync(gostCfg, gatewayCfg, "eth0").ConfigureAwait(false);
        Assert.IsTrue(changed, "A renamed changed server country code name should result in a changed since the related pool is updated");
        Assert.IsTrue(relayJson.Remove(modRelay), "Failed to remove mod relay");

        modRelay = removedRelay with { CityCode = "test" + removedRelay.CityCode };
        relayJson.Insert(0, modRelay);
        await File.WriteAllTextAsync(GostProxySync.RelayFile, JsonSerializer.Serialize(relayJson)).ConfigureAwait(false);
        changed = await GostProxySync.UpdateMullvadServersAsync(gostCfg, gatewayCfg, "eth0").ConfigureAwait(false);
        Assert.IsTrue(changed, "A changed relay server city code should result in a changed since the related pool is updated");
        Assert.IsTrue(relayJson.Remove(modRelay), "Failed to remove mod relay");

        modRelay = removedRelay with { SocksPort = removedRelay.SocksPort + 1 };
        relayJson.Insert(0, modRelay);
        await File.WriteAllTextAsync(GostProxySync.RelayFile, JsonSerializer.Serialize(relayJson)).ConfigureAwait(false);
        changed = await GostProxySync.UpdateMullvadServersAsync(gostCfg, gatewayCfg, "eth0").ConfigureAwait(false);
        Assert.IsTrue(changed, "A changed relay server city port should result in a changed since the related pool is updated");
        Assert.IsTrue(relayJson.Remove(modRelay), "Failed to remove mod relay");
        await File.WriteAllTextAsync(GostProxySync.RelayFile, JsonSerializer.Serialize(relayJson)).ConfigureAwait(false);
    }

    private async Task<bool> IsRelaysApiReachable()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(GostProxySync.RelayApiUrl, cts.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<IReadOnlyCollection<MullvadRelay>?> GetMullvadRelaysAsync()
    {
        var relayJson = await GetEmbeddedRelayJsonAsync().ConfigureAwait(false);
        if (relayJson == null) return null;
        return JsonSerializer.Deserialize<List<MullvadRelay>>(relayJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private async Task<string?> GetEmbeddedRelayJsonAsync()
    {
        await using var stream = GetType().Assembly.GetManifestResourceStream("GostGen.Tests.TestData.mullvad.json");
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }
}