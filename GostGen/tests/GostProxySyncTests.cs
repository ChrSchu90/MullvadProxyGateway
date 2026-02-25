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
        // New pool
        var config = new GostConfig();
        var port = GostProxySync.FindPoolAreaPort(config, "test-pool-1");
        var expectedPort = GostProxySync.ProxyPortCitiesStart;
        Assert.AreEqual(expectedPort, port, "Empty config should result in area start port");

        // Add pool
        config = new GostConfig { Services = [new ServiceConfig { Name = "test-pool", Addr = $":{GostProxySync.ProxyPortCitiesStart}" }] };
        port = GostProxySync.FindPoolAreaPort(config, "test-pool-1");
        expectedPort = GostProxySync.ProxyPortCitiesStart + GostProxySync.ProxyPortsPerCity;
        Assert.AreEqual(expectedPort, port, "New pool has not been created after existing pool");

        // Reuse same pool
        config = new GostConfig
        {
            Services = [
                           new ServiceConfig { Name = "test-pool-1", Addr = $":{GostProxySync.ProxyPortCitiesStart}"},
                           new ServiceConfig { Name = "test-pool-2", Addr = $":{GostProxySync.ProxyPortCitiesStart + 100}"}
                       ]
        };
        port = GostProxySync.FindPoolAreaPort(config, "test-pool-1");
        expectedPort = GostProxySync.ProxyPortCitiesStart;
        Assert.AreEqual(expectedPort, port, "Same pool does not return previous port");

        // Add pool at end with gap
        config = new GostConfig
        {
            Services = [
                                 new ServiceConfig { Name = "test-pool-1", Addr = $":{GostProxySync.ProxyPortCitiesStart}"},
                                 new ServiceConfig { Name = "test-pool-2", Addr = $":{GostProxySync.ProxyPortCitiesStart + 100}"}
                             ]
        };
        port = GostProxySync.FindPoolAreaPort(config, "test-pool-3");
        expectedPort = GostProxySync.ProxyPortCitiesStart + 100 + GostProxySync.ProxyPortsPerCity;
        Assert.AreEqual(expectedPort, port, "Pool was not added at the very end");

        // Out of area
        config = new GostConfig
        {
            Services = [
                                 new ServiceConfig { Name = "test-pool-1", Addr = $":{GostProxySync.ProxyPortCitiesStart}"},
                                 new ServiceConfig { Name = "test-pool-2", Addr = $":{GostProxySync.ProxyPortCitiesEnd - GostProxySync.ProxyPortsPerCity}"}
                             ]
        };
        port = GostProxySync.FindPoolAreaPort(config, "test-pool-3");
        expectedPort = -1;
        Assert.AreEqual(expectedPort, port, "Pool was added outside of area");
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
    /// Tests for <see cref="GostProxySync.UpdateMullvadServersAsync"/>
    /// </summary>
    [TestMethod]
    public async Task UpdateMullvadServersAsync()
    {
        var gatewayCfg = new GatewayConfig { AlwaysGenerateServers = false };
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
        Assert.IsFalse(changed, "As long as AlwaysGenerateServers is false the proxies should not be updated if any exists");

        gatewayCfg.AlwaysGenerateServers = true;
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