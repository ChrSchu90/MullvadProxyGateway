namespace GostGen;

using GostGen.DTO;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

/// <summary>
/// Synchronizes the <see cref="GostConfig"/> local proxy configuration with the <see cref="GatewayConfig"/>
/// </summary>
internal class GostProxySync
{
    internal const string ExportCsvFile = "data/proxies.csv";
    internal const string ExportJsonFile = "data/proxies.json";
    
    internal const string ServiceLocalName = "service-local";
    internal const string SocksType = "socks5";
    internal const string NetworkProtocol = "tcp";

    internal const int ProxyPortLocal = 1080;
    internal const int ProxyPortCitiesStart = 2000;
    internal const int ProxyPortCitiesEnd = 3000;
    internal const int ProxiesServersPerCity = 9;
    internal const int ProxyPortsPerCity = ProxiesServersPerCity + 1;
    internal const int PoolSelectorMaxFails = 1;
    internal const string PoolSelectorFailTimeout = "10s";
    internal const SelectorStrategy PoolSelectorStrategy = SelectorStrategy.round;

    internal static readonly Regex AddressPortRegex = new(@":(?<port>\d+)$", RegexOptions.Compiled);

    /// <summary>
    /// Updates the local proxy inside the <see cref="GostConfig" />.
    /// </summary>
    /// <param name="gostConfig">The GOST configuration.</param>
    /// <param name="gatewayConfig">The gateway configuration.</param>
    /// <returns><c>true</c> if the <see cref="GostConfig" /> has been changed.</returns>
    public static async Task<bool> UpdateAsync(GostConfig gostConfig, GatewayConfig gatewayConfig)
    {
        var networkInterface = GetDefaultInterface();
        var changed = await UpdateLocalProxyAsync(gostConfig, gatewayConfig, networkInterface).ConfigureAwait(false);
        changed |= await UpdateMullvadServersAsync(gostConfig, gatewayConfig, networkInterface).ConfigureAwait(false);
        return changed;
    }

    internal static Task<bool> UpdateLocalProxyAsync(GostConfig gostConfig, GatewayConfig gatewayConfig, string networkInterface)
    {
        var changed = false;
        gostConfig.Services ??= [];
        var address = $":{ProxyPortLocal}";
        var service = gostConfig.Services.FirstOrDefault(s => string.Equals(s.Name, ServiceLocalName));
        if (service == null)
        {
            service = new() { Name = ServiceLocalName };
            gostConfig.Services.Add(service);
        }

        if (!string.Equals(service.Addr, address) ||
            !string.Equals(service.Interface, networkInterface) ||
            !string.Equals(service.Listener?.Type, NetworkProtocol) ||
            !string.Equals(service.Handler?.Type, SocksType) ||
            !string.Equals(service.Handler?.Auther, GostUserSync.AutherInternalGroup))
        {
            Log.Debug($"Updating local proxy configuration `{ServiceLocalName}`");
            service.Addr = address;
            service.Interface = networkInterface;
            service.Listener = new() { Type = NetworkProtocol };
            service.Handler = new() { Type = SocksType, Auther = GostUserSync.AutherInternalGroup };
            changed = true;
        }

        return Task.FromResult(changed);
    }

    internal static async Task<bool> UpdateMullvadServersAsync(GostConfig gostConfig, GatewayConfig gatewayConfig, string networkInterface)
    {
        if (!gatewayConfig.AlwaysGenerateServers && gostConfig.Services?.Any() == true && gostConfig.Chains?.Any() == true)
        {
            Log.Information("Skip update of GOST servers");
            return false;
        }

        var relays = await GetMullvadRelaysAsync().ConfigureAwait(false);
        if (relays?.Any() != true) return false;

        var cfgChanged = false;
        ICollection<Proxy> proxies = new List<Proxy>();
        Log.Information("Start to create/update GOST servers");
        var countries = relays.Where(r => !string.IsNullOrWhiteSpace(r.CountryName)).OrderBy(r => r.CountryName).GroupBy(r => r.CountryName).ToArray();
        foreach (var country in countries)
        {
            var cities = country.Where(r => !string.IsNullOrWhiteSpace(r.CityName)).OrderBy(r => r.CityName).GroupBy(r => r.CityName).Where(g => g.Any()).ToArray();
            Log.Verbose($"Found {cities.Length} cities in {country.Key}");
            foreach (var city in cities)
            {
                var servers = city.Where(s => !string.IsNullOrWhiteSpace(s.SocksName)).OrderBy(c => c.Hostname).ToArray();
                if (!servers.Any()) continue;
                cfgChanged |= CreateOrUpdateProxy(gostConfig, city.First().CountryCode!, city.First().CityCode!, servers, networkInterface, ref proxies);
            }
        }

        if (cfgChanged || !File.Exists(ExportCsvFile) || new FileInfo(ExportCsvFile).Length < 1)
            ExportProxyCsv(proxies);
        if (cfgChanged || !File.Exists(ExportJsonFile) || new FileInfo(ExportJsonFile).Length < 1)
            ExportProxyJson(proxies);

        return cfgChanged;
    }

    internal static async Task<IReadOnlyCollection<MullvadRelay>?> GetMullvadRelaysAsync()
    {
        const string MullvadFile = "data/mullvad.json";
        if (File.Exists(MullvadFile) && new FileInfo(MullvadFile).Length > 0)
        {
            try
            {
                Log.Verbose($"Lodaing Mullvad relays from file `{MullvadFile}`");
                var relays = JsonSerializer.Deserialize<List<MullvadRelay>>(await File.ReadAllTextAsync(MullvadFile), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Log.Information($"Found {relays?.Count ?? 0} Mullvad relays", relays?.Count ?? 0);
                return relays;
            }
            catch (Exception err)
            {
                Log.Fatal(err, $"Error on loading Mullvad relays from file `{MullvadFile}`");
                return null;
            }
        }

        try
        {
            const string ApiUrl = "https://api.mullvad.net/www/relays/wireguard";
            Log.Information($"Downloading Mullvad relays from {ApiUrl}");
            using var httpClient = new HttpClient();
            var json = await httpClient.GetStringAsync(ApiUrl).ConfigureAwait(false);
            var relays = JsonSerializer.Deserialize<List<MullvadRelay>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Log.Verbose($"Downloaded Mullvad relays:\n{json}");
            Log.Information($"Found {relays?.Count ?? 0} Mullvad WireGuard relays", relays?.Count ?? 0);
            return relays;
        }
        catch (Exception err)
        {
            Log.Fatal(err, "Error on download Mullvad relays");
            return null;
        }
    }

    internal static bool CreateOrUpdateProxy(GostConfig gostConfig, string countryCode, string cityCode, MullvadRelay[] servers, string networkInterface, ref ICollection<Proxy> proxies)
    {
        var cfgChanged = false;
        var servicePoolName = $"service-{countryCode}-{cityCode}-pool".ToLower();
        var chainPoolName = $"chain-{countryCode}-{cityCode}-pool".ToLower();
        var chainPoolHopName = $"hop-{chainPoolName}".ToLower();
        var poolPort = FindNextFreeCityPortAreaStart(gostConfig, servicePoolName);
        if (poolPort < 1) return false;
        var poolServiceAddress = $":{poolPort}";

        gostConfig.Services ??= [];
        var poolService = gostConfig.Services.FirstOrDefault(s => string.Equals(s.Name, servicePoolName, StringComparison.OrdinalIgnoreCase));
        if (poolService == null)
        {
            Log.Debug($"Adding proxy pool service `{servicePoolName}`");
            poolService = new() { Name = servicePoolName };
            gostConfig.Services.Add(poolService);
            cfgChanged = true;
        }

        proxies.Add(new Proxy(true, servers.First(), poolService));

        if (poolService.Addr != poolServiceAddress ||
            !string.Equals(poolService.Interface, networkInterface) ||
            !string.Equals(poolService.Listener?.Type, NetworkProtocol) ||
            !string.Equals(poolService.Handler?.Type, SocksType) ||
            !string.Equals(poolService.Handler?.Auther, GostUserSync.AutherMullvadGroup) ||
            !string.Equals(poolService.Handler?.Chain, chainPoolName))
        {
            Log.Debug($"Updating proxy pool `{servicePoolName}`");
            poolService.Addr = poolServiceAddress;
            poolService.Interface = networkInterface;
            poolService.Listener = new() { Type = NetworkProtocol };
            poolService.Handler = new() { Type = SocksType, Auther = GostUserSync.AutherMullvadGroup, Chain = chainPoolName };
            cfgChanged = true;
        }

        gostConfig.Chains ??= [];
        var poolChain = gostConfig.Chains.FirstOrDefault(c => string.Equals(c.Name, chainPoolName, StringComparison.OrdinalIgnoreCase));
        if (poolChain == null)
        {
            Log.Debug($"Adding proxy pool chain `{chainPoolName}`");
            poolChain = new() { Name = chainPoolName };
            gostConfig.Chains.Add(poolChain);
            cfgChanged = true;
        }

        poolChain.Hops ??= [];
        var poolHop = poolChain.Hops.FirstOrDefault(h => string.Equals(h.Name, chainPoolHopName, StringComparison.OrdinalIgnoreCase));
        if (poolHop == null)
        {
            Log.Debug($"Adding pool hop `{chainPoolHopName}`");
            poolHop = new() { Name = chainPoolHopName };
            poolChain.Hops.Add(poolHop);
            cfgChanged = true;
        }

        poolHop.Nodes ??= [];
        poolHop.Selector ??= new();
        if (poolHop.Selector.Strategy != PoolSelectorStrategy ||
            poolHop.Selector.MaxFails != PoolSelectorMaxFails ||
            poolHop.Selector.FailTimeout != PoolSelectorFailTimeout)
        {
            Log.Debug($"Updating pool selector of `{chainPoolHopName}`");
            poolHop.Selector.Strategy = PoolSelectorStrategy;
            poolHop.Selector.MaxFails = PoolSelectorMaxFails;
            poolHop.Selector.FailTimeout = PoolSelectorFailTimeout;
            cfgChanged = true;
        }

        var serverCnt = Math.Min(servers.Count(), ProxiesServersPerCity);
        for (var i = 1; i <= serverCnt; i++)
        {
            var serviceCityName = $"service-{countryCode}-{cityCode}-{i}".ToLower();
            var chainCityName = $"chain-{countryCode}-{cityCode}-{i}".ToLower();
            var chainCityHopName = $"hop-{chainCityName}";
            var chainCityHopNodeName = $"node-{chainCityHopName}";
            var cityAddress = $":{poolPort + i}";
            var cityServer = servers.ElementAt(i - 1);

            var cityService = gostConfig.Services.FirstOrDefault(s => string.Equals(s.Name, serviceCityName, StringComparison.OrdinalIgnoreCase));
            if (cityService == null)
            {
                Log.Debug($"Adding proxy city `{serviceCityName}`");
                cityService = new() { Name = serviceCityName };
                gostConfig.Services.Add(cityService);
                cfgChanged = true;
            }

            proxies.Add(new Proxy(false, cityServer, cityService));

            if (cityService.Addr != cityAddress ||
                !string.Equals(cityService.Interface, networkInterface) ||
                !string.Equals(cityService.Listener?.Type, NetworkProtocol) ||
                !string.Equals(cityService.Handler?.Type, SocksType) ||
                !string.Equals(cityService.Handler?.Auther, GostUserSync.AutherMullvadGroup) ||
                !string.Equals(cityService.Handler?.Chain, chainCityName))
            {
                Log.Debug($"Updating proxy city `{servicePoolName}`");
                cityService.Addr = cityAddress;
                cityService.Interface = networkInterface;
                cityService.Listener = new() { Type = NetworkProtocol };
                cityService.Handler = new() { Type = SocksType, Auther = GostUserSync.AutherMullvadGroup, Chain = chainCityName };
                cfgChanged = true;
            }

            var chain = gostConfig.Chains.FirstOrDefault(c => string.Equals(c.Name, chainCityName, StringComparison.OrdinalIgnoreCase));
            if (chain == null)
            {
                Log.Debug($"Adding proxy chain `{chainCityName}`");
                chain = new() { Name = chainCityName };
                gostConfig.Chains.Add(chain);
                cfgChanged = true;
            }

            chain.Hops ??= [];
            var hop = chain.Hops.FirstOrDefault(h => string.Equals(h.Name, chainCityHopName, StringComparison.OrdinalIgnoreCase));
            if (hop == null)
            {
                Log.Debug($"Adding city hop `{chainCityHopName}`");
                hop = new() { Name = chainCityHopName };
                chain.Hops.Add(hop);
                cfgChanged = true;
            }

            hop.Nodes ??= [];
            var node = hop.Nodes.FirstOrDefault(n => string.Equals(n.Name, chainCityHopNodeName, StringComparison.OrdinalIgnoreCase));
            if (node == null)
            {
                Log.Debug($"Adding city node `{chainCityHopNodeName}`");
                node = new() { Name = chainCityHopNodeName };
                hop.Nodes.Add(node);
                cfgChanged = true;
            }

            var svrAddress = $"{cityServer.SocksName}:{cityServer.SocksPort}";
            if (!string.Equals(node.Bypass, GostBypassSync.BypassMullvadGroup) ||
                !string.Equals(node.Addr, svrAddress) ||
                !string.Equals(node.Connector?.Type, SocksType) ||
                !string.Equals(node.Dialer?.Type, NetworkProtocol))
            {
                Log.Debug($"Updating city node `{chainCityHopNodeName}`");
                node.Bypass = GostBypassSync.BypassMullvadGroup;
                node.Addr = svrAddress;
                node.Connector = new() { Type = SocksType };
                node.Dialer = new() { Type = NetworkProtocol };
                cfgChanged = true;
            }

            var poolNode = node with { Name = $"{chainCityHopNodeName}-pool" };
            if (poolHop.Nodes.ElementAtOrDefault(i - 1) != poolNode)
            {
                Log.Debug($"Updating pool node `{poolNode.Name}`");
                poolHop.Nodes.Insert(i - 1, poolNode);
                cfgChanged = true;
            }
        }

        return cfgChanged;
    }

    internal static int FindNextFreeCityPortAreaStart(GostConfig gostConfig, string servicePoolName)
    {
        gostConfig.Services ??= [];

        // Reuse existing poot port
        var service = gostConfig.Services.FirstOrDefault(s => string.Equals(s.Name, servicePoolName, StringComparison.OrdinalIgnoreCase));
        if (AddressPortRegex.IsMatch(service?.Addr ?? string.Empty))
            return int.Parse(AddressPortRegex.Match(service!.Addr!).Groups["port"].Value);

        // ToDo: More efficient way to find free port area that also supports free areas between used ports (e.g. 2000-2009 used, 2010-2019 free, 2020-2029 used -> assign next pool to 2010-2019)

        // Find next free port area
        var usedPorts = gostConfig.Services.Where(s => AddressPortRegex.IsMatch(s.Addr ?? string.Empty))
            .Select(s => int.Parse(AddressPortRegex.Match(s.Addr!).Groups["port"].Value))
            .Where(p => p >= ProxyPortCitiesStart).ToArray();
        if (!usedPorts.Any()) return ProxyPortCitiesStart;
        var lastPort = usedPorts.Max();
        if (lastPort + 1 % ProxyPortsPerCity != 0) lastPort = (lastPort / ProxyPortsPerCity + 1) * ProxyPortsPerCity; // Round up to next multiple of ProxyPortsPerCity (10)
        lastPort = Math.Max(lastPort, ProxyPortCitiesStart);    // Limit to start of city proxy port area
        if (lastPort + ProxyPortsPerCity < ProxyPortCitiesEnd)  // Limit to end of city proxy port area
            return lastPort;

        Log.Error($"Unable to find free port area for city pool `{servicePoolName}` since last used port `{lastPort}` is to close to end of city proxy port area ({ProxyPortCitiesStart}-{ProxyPortCitiesEnd})");
        return -1;
    }

    internal static string GetDefaultInterface()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
                   .Where(i =>
                       i.OperationalStatus == OperationalStatus.Up &&
                       i.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                       i.GetIPProperties().GatewayAddresses.Any(g => !Equals(g.Address, IPAddress.Any)))
                   .Select(i => i.Name)
                   .FirstOrDefault() ?? "eth0";
    }

    internal static void ExportProxyCsv(ICollection<Proxy> proxies)
    {
        try
        {
            Log.Information($"Exporting proxy list to `{ExportCsvFile}`");
            var csvString = "Country,City,Location Code,Port,Target\n" + string.Join("\n",
                                proxies.Select(p => $"\"{p.Server.CountryName}\"," +
                                                    $"\"{p.Server.CityName}\"," +
                                                    $"{p.Server.CountryCode}-{p.Server.CityCode}," +
                                                    $"{AddressPortRegex.Match(p.Service.Addr!).Groups["port"]}," +
                                                    $"{(p.IsPool ? "random" : p.Server.SocksName)}")) + "\n";

            File.WriteAllText(ExportCsvFile, csvString);
        }
        catch (Exception err)
        {
            Log.Fatal(err, $"Error on exporting proxy csv {ExportCsvFile}");
        }
    }

    internal static void ExportProxyJson(ICollection<Proxy> proxies)
    {
        try
        {
            Log.Information($"Exporting proxy list to `{ExportJsonFile}`");
            var export = proxies.Select(p => new
            {
                Country = p.Server.CountryName,
                City = p.Server.CityName,
                LocationCode = $"{p.Server.CountryCode}-{p.Server.CityCode}",
                Port = int.Parse(AddressPortRegex.Match(p.Service.Addr!).Groups["port"].Value),
                Target = p.IsPool ? "random" : p.Server.SocksName
            });

            File.WriteAllText(ExportJsonFile, JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception err)
        {
            Log.Fatal(err, $"Error on exporting proxy json {ExportJsonFile}");
        }
    }
    
    internal record Proxy(bool IsPool, MullvadRelay Server, ServiceConfig Service);
}