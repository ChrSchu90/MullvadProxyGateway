namespace GostGen;

using GostGen.DTO;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    internal const string RelayApiUrl = "https://api.mullvad.net/www/relays/wireguard";
    internal const string RelayFile = "data/mullvad.json";
    internal const string ExportCsvFile = "data/proxies.csv";
    internal const string ExportJsonFile = "data/proxies.json";

    internal const string ServiceLocalName = "service-local";
    internal const string SocksType = "socks5";
    internal const string NetworkProtocol = "tcp";

    internal const int ProxyPortLocal = 1080;
    internal const int ProxyPortStart = 2000;
    internal const int ProxyPortEnd = 5000;

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
        if (!gatewayConfig.UpdateServersOnStartup && gostConfig.Services?.Any() == true && gostConfig.Chains?.Any() == true)
        {
            Log.Information("Skip update of GOST servers");
            return false;
        }

        var relays = await GetMullvadRelaysAsync().ConfigureAwait(false);
        if (relays?.Any() != true) return false;
        relays = ApplyProxyFilter(relays, gatewayConfig);

        var cfgChanged = false;
        ICollection<Proxy> proxies = new List<Proxy>();
        Log.Information("Start to create/update GOST servers");
        var countries = relays.Where(r => !string.IsNullOrWhiteSpace(r.CountryName)).OrderBy(r => r.CountryName).GroupBy(r => r.CountryName).ToArray();
        foreach (var country in countries)
        {
            var cities = country.Where(r => !string.IsNullOrWhiteSpace(r.CityName)).OrderBy(r => r.CityName).GroupBy(r => r.CityName).Where(g => g.Any()).ToArray();
            foreach (var city in cities)
            {
                var servers = city.Where(s => !string.IsNullOrWhiteSpace(s.SocksName)).OrderBy(c => c.Hostname).ToArray();
                if (!servers.Any()) continue;
                cfgChanged |= CreateOrUpdateProxy(gostConfig, gatewayConfig, city.First().CountryCode!, city.First().CityCode!, servers, networkInterface, ref proxies);
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
        List<MullvadRelay>? relays;
        if (File.Exists(RelayFile) && new FileInfo(RelayFile).Length > 0)
        {
            try
            {
                Log.Verbose($"Loading Mullvad relays from file `{RelayFile}`");
                relays = JsonSerializer.Deserialize<List<MullvadRelay>>(await File.ReadAllTextAsync(RelayFile), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception err)
            {
                Log.Fatal(err, $"Error on loading Mullvad relays from file `{RelayFile}`");
                return null;
            }
        }
        else
        {
            try
            {
                Log.Information($"Downloading Mullvad relays from {RelayApiUrl}");
                using var httpClient = new HttpClient();
                var json = await httpClient.GetStringAsync(RelayApiUrl).ConfigureAwait(false);
                relays = JsonSerializer.Deserialize<List<MullvadRelay>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Log.Verbose($"Downloaded Mullvad relays:\n{json}");
            }
            catch (Exception err)
            {
                Log.Fatal(err, "Error on download Mullvad relays");
                return null;
            }
        }

        if (relays?.Any() != true) return relays;
        Log.Information($"Mullvad relays found: {relays.Count} total (" +
                        $"{relays.GroupBy(r => r.CountryCode).Count()} countries, " +
                        $"{relays.GroupBy(r => r.CityCode).Count()} cities, " +
                        $"{relays.Count(r => !string.IsNullOrWhiteSpace(r.SocksName))} proxy endpoints; " +
                        $"max {relays.GroupBy(r => r.CityCode).Max(c => c.Count())} endpoints per city)");

        return relays;
    }

    internal static bool CreateOrUpdateProxy(GostConfig gostConfig, GatewayConfig gatewayConfig, string countryCode, string cityCode, MullvadRelay[] servers, string networkInterface, ref ICollection<Proxy> proxies)
    {
        var cfgChanged = false;
        var startPort = FindPoolAreaPort(gostConfig, gatewayConfig, countryCode, cityCode);
        if (startPort < ProxyPortStart) return false;
        var addPool = gatewayConfig.CityRandomPools;

        gostConfig.Services ??= [];
        gostConfig.Chains ??= [];

        // Remove services and chains that exceeds the max servers per city
        var maxServiceAmount = Math.Min(addPool ? servers.Length + 1 : servers.Length, gatewayConfig.MaxServersPerCity);
        gostConfig.Services.RemoveAll(s =>
        {
            var match = AddressPortRegex.Match(s.Addr ?? string.Empty);
            var sericePort = int.Parse(match.Groups["port"].Value);
            var remove = !match.Success || string.IsNullOrWhiteSpace(s.Name) ||
                         (s.Name.StartsWith($"service-{countryCode}-{cityCode}-") &&
                          sericePort > ProxyPortStart &&   // Ignore internal proxy
                          sericePort > startPort &&        // Remove area start
                          sericePort >= startPort + maxServiceAmount);   // Remove area end
            if (remove)
            {
                Log.Debug($"Removing service `{s.Name}` and its chain `{s.Handler?.Chain}`");
                if (!string.IsNullOrWhiteSpace(s.Handler?.Chain))
                    gostConfig.Chains.RemoveAll(r => r.Name == s.Handler?.Chain);
                cfgChanged = true;
            }

            return remove;
        });

        for (var i = 0; i <= servers.Length; i++)
        {
            var isPool = addPool && i == 0;
            if ((!addPool && i > gatewayConfig.MaxServersPerCity) ||
                (!addPool && i >= servers.Length) ||
                i >= gatewayConfig.MaxServersPerCity) break;

            var serviceName = addPool ? isPool ? $"service-{countryCode}-{cityCode}-pool" : $"service-{countryCode}-{cityCode}-{i}" : $"service-{countryCode}-{cityCode}-{i + 1}";
            var chainName = addPool ? isPool ? $"chain-{countryCode}-{cityCode}-pool" : $"chain-{countryCode}-{cityCode}-{i}" : $"chain-{countryCode}-{cityCode}-{i + 1}";
            var hopName = $"hop-{chainName}";
            var serviceAddr = addPool ? isPool ? $":{startPort}" : $":{startPort + i}" : $":{startPort + i}";

            var service = gostConfig.Services.FirstOrDefault(s => string.Equals(s.Addr, serviceAddr));
            if (service == null)
            {
                Log.Debug($"Adding service `{serviceName}`");
                service = new() { Addr = serviceAddr };
                gostConfig.Services.Add(service);
                cfgChanged = true;
            }

            service.Handler ??= new();
            service.Listener ??= new();
            if (!string.Equals(service.Name, serviceName) ||
                !string.Equals(service.Interface, networkInterface) ||
                !string.Equals(service.Listener.Type, NetworkProtocol) ||
                !string.Equals(service.Handler.Type, SocksType) ||
                !string.Equals(service.Handler.Auther, GostUserSync.AutherMullvadGroup))
            {
                Log.Debug($"Update service `{serviceName}`");
                service.Name = serviceName;
                service.Interface = networkInterface;
                service.Listener.Type = NetworkProtocol;
                service.Handler.Type = SocksType;
                service.Handler.Auther = GostUserSync.AutherMullvadGroup;
                cfgChanged = true;
            }

            var chain = !string.IsNullOrWhiteSpace(service.Handler.Chain) ?
                            gostConfig.Chains.FirstOrDefault(c => string.Equals(c.Name, service.Handler.Chain)) :
                            null;
            if (chain == null)
            {
                Log.Debug($"Adding server chain `{chainName}`");
                chain = new ChainConfig { Name = chainName };
                gostConfig.Chains.Add(chain);
                cfgChanged = true;
            }

            if (!string.Equals(chain.Name, chainName) ||
                !string.Equals(service.Handler.Chain, chainName))
            {
                Log.Debug($"Updating server chain `{chainName}`");
                chain.Name = chainName;
                service.Handler.Chain = chainName;
                cfgChanged = true;
            }

            chain.Hops ??= []; // Only 1 hop should always exist
            if (chain.Hops.Count > 1)
            {
                Log.Debug($"Cleanup of hops in chain `{chainName}`");
                chain.Hops.RemoveRange(1, chain.Hops.Count - 1);
                cfgChanged = true;
            }

            var hop = chain.Hops.FirstOrDefault();
            if (hop == null)
            {
                Log.Debug($"Adding server hop `{hopName}`");
                hop = new HopConfig { Name = hopName };
                chain.Hops.Add(hop);
                cfgChanged = true;
            }

            if (!string.Equals(hop.Name, hopName))
            {
                Log.Debug($"Updating server hop `{hopName}`");
                hop.Name = hopName;
                cfgChanged = true;
            }

            // Single server hops should not have selectors, since there is only 1 node, so remove selector if it exists
            if (!isPool)
            {
                if (hop.Selector != null)
                {
                    Log.Debug($"Updating server hop selector of `{hopName}`");
                    hop.Selector = null;
                    cfgChanged = true;
                }
            }
            else
            {
                hop.Selector ??= new();
                if (hop.Selector.Strategy != PoolSelectorStrategy ||
                    hop.Selector.MaxFails != PoolSelectorMaxFails ||
                    hop.Selector.FailTimeout != PoolSelectorFailTimeout)
                {
                    Log.Debug($"Updating hop selector of `{hopName}`");
                    hop.Selector.Strategy = PoolSelectorStrategy;
                    hop.Selector.MaxFails = PoolSelectorMaxFails;
                    hop.Selector.FailTimeout = PoolSelectorFailTimeout;
                    cfgChanged = true;
                }

            }

            hop.Nodes ??= []; 
            if ((!isPool && hop.Nodes.Count > 1) || // Only 1 node should exist for single servers, 
                (isPool && hop.Nodes.Count > servers.Length)) // clear pool only if more nodes are defined than servers available
            {
                Log.Debug($"Clearing server hop notes of `{hopName}`");
                hop.Nodes.Clear();
                cfgChanged = true;
            }

            bool AddServer(int nodeIndex, MullvadRelay server, ref ICollection<Proxy> proxies)
            {
                var changed = false;
                var svrAddress = $"{server.SocksName}:{server.SocksPort}";
                var nodeName = isPool ? $"node-{hopName}-{nodeIndex + 1}" : $"node-{hopName}";

                var node = hop.Nodes.ElementAtOrDefault(nodeIndex);
                if (node == null)
                {
                    Log.Debug($"Adding pool hop `{hopName}`");
                    node = new() { Name = nodeName };
                    hop.Nodes.Add(node);
                    changed = true;
                }

                if (!string.Equals(node.Name, nodeName) ||
                    !string.Equals(node.Bypass, GostBypassSync.BypassMullvadGroup) ||
                    !string.Equals(node.Addr, svrAddress) ||
                    !string.Equals(node.Connector?.Type, SocksType) ||
                    !string.Equals(node.Dialer?.Type, NetworkProtocol))
                {
                    Log.Debug($"Updating server hop `{nodeName}`");
                    node.Name = nodeName;
                    node.Bypass = GostBypassSync.BypassMullvadGroup;
                    node.Addr = svrAddress;
                    node.Connector = new() { Type = SocksType };
                    node.Dialer = new() { Type = NetworkProtocol };
                    changed = true;
                }

                proxies.Add(new Proxy(isPool ? 0 : i, server, service, chain, hop));
                return changed;
            }

            // Add servers to nodes depending on pool or single server
            if (isPool)
                for (var ni = 0; ni < servers.Length; ni++)
                    cfgChanged |= AddServer(ni, servers[ni], ref proxies);
            else
                cfgChanged |= AddServer(0, addPool ? servers[i - 1] : servers[i], ref proxies);

        }

        if (!cfgChanged) return cfgChanged;
        gostConfig.Services.Sort((a, b) => a.Addr!.CompareTo(b.Addr));
        gostConfig.Chains.Sort((a, b) => a.Name!.CompareTo(b.Name));
        return cfgChanged;
    }

    internal static int FindPoolAreaPort(GostConfig gostConfig, GatewayConfig gatewayConfig, string countryCode, string cityCode)
    {
        gostConfig.Services ??= [];
        var proxyPortsPerCity = gatewayConfig.MaxServersPerCity;
        var serviceRegex = new Regex($"^service-{countryCode}-{cityCode}-(\\d+|pool)$", RegexOptions.IgnoreCase);

        // Reuse existing pool port
        var existingService = gostConfig.Services.FirstOrDefault(s => serviceRegex.IsMatch(s.Name ?? string.Empty));
        if (AddressPortRegex.IsMatch(existingService?.Addr ?? string.Empty))
            return int.Parse(AddressPortRegex.Match(existingService!.Addr!).Groups["port"].Value);

        // Get all used ports for dynamic proxies
        var portNumbersUsed = gostConfig.Services.Where(s => AddressPortRegex.IsMatch(s.Addr ?? string.Empty))
            .Select(s => int.Parse(AddressPortRegex.Match(s.Addr!).Groups["port"].Value))
            .Where(p => p >= ProxyPortStart).ToArray();

        // Use start port if no port is already in use
        if (!portNumbersUsed.Any())
            return ProxyPortStart;

        // Round up to next multiple of proxyPortsPerCity, this way there is a pattern inside the dynamic proxy ports
        var lastUsedPort = portNumbersUsed.Max();
        var nextPortFree = ProxyPortStart +
                           ((Math.Max(lastUsedPort, ProxyPortStart - 1) - ProxyPortStart) / proxyPortsPerCity + 1) * proxyPortsPerCity;

        // Check min/max port limits
        nextPortFree = Math.Max(nextPortFree, ProxyPortStart);
        if (nextPortFree + proxyPortsPerCity < ProxyPortEnd)
            return nextPortFree;

        Log.Error($"Unable to find free port `{countryCode}-{cityCode}` since last used port `{nextPortFree + proxyPortsPerCity}` exceeds end of city proxy port area ({ProxyPortStart}-{ProxyPortEnd})");
        return -1;
    }

    [ExcludeFromCodeCoverage]
    internal static string GetDefaultInterface()
    {
        // ToDo option to define interface inside config?
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
            var csvString = "Country,Country Code,City,City Code,City No.,Location Code,Port,Target\n" + string.Join("\n",
                                proxies.Select(p => $"\"{p.Server.CountryName}\"," +
                                                    $"{p.Server.CountryCode}," +
                                                    $"\"{p.Server.CityName}\"," +
                                                    $"{p.Server.CityCode}," +
                                                    $"{p.CityIdx}," +
                                                    $"{p.Server.CountryCode}-{p.Server.CityCode}," +
                                                    $"{AddressPortRegex.Match(p.Service.Addr!).Groups["port"]}," +
                                                    $"{(p.CityIdx == 0 ? "random" : p.Server.SocksName)}")) + "\n";

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
                CountryCode = p.Server.CountryCode,
                City = p.Server.CityName,
                CityCode = p.Server.CityCode,
                CityNo = p.CityIdx,
                LocationCode = $"{p.Server.CountryCode}-{p.Server.CityCode}",
                Port = int.Parse(AddressPortRegex.Match(p.Service.Addr!).Groups["port"].Value),
                Target = p.CityIdx == 0 ? "random" : p.Server.SocksName
            });

            File.WriteAllText(ExportJsonFile, JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception err)
        {
            Log.Fatal(err, $"Error on exporting proxy json {ExportJsonFile}");
        }
    }

    internal static IReadOnlyCollection<MullvadRelay> ApplyProxyFilter(IReadOnlyCollection<MullvadRelay> relays, GatewayConfig gatewayConfig)
    {
        var countryInclude = new HashSet<string>(gatewayConfig.ProxyFilter.Country.Include.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
        var countryExclude = new HashSet<string>(gatewayConfig.ProxyFilter.Country.Exclude.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
        var cityInclude = new HashSet<string>(gatewayConfig.ProxyFilter.City.Include.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
        var cityExclude = new HashSet<string>(gatewayConfig.ProxyFilter.City.Exclude.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);

        var filterdRelays = relays.Where(r =>
            {
                if (string.IsNullOrWhiteSpace(r.CountryName) ||
                   string.IsNullOrWhiteSpace(r.CountryCode) ||
                   string.IsNullOrWhiteSpace(r.CityName) ||
                   string.IsNullOrWhiteSpace(r.CityCode))
                    return false;

                if (gatewayConfig.ProxyFilter.OwnedOnly && !r.Owned)
                    return false;

                if (countryInclude.Count > 0 &&
                    !countryInclude.Contains(r.CountryCode) &&
                    !countryInclude.Contains(r.CountryName))
                    return false;

                if (countryExclude.Contains(r.CountryCode) ||
                    countryExclude.Contains(r.CountryName))
                    return false;

                if (cityInclude.Count > 0 &&
                    !cityInclude.Contains(r.CityCode) &&
                    !cityInclude.Contains(r.CityName))
                    return false;

                if (cityExclude.Contains(r.CityCode) ||
                    cityExclude.Contains(r.CityName))
                    return false;

                return true;
            }).ToArray();

        Log.Information($"Filtered {relays.Count} Mullvad relays, {filterdRelays.Length} relays remaining");
        return filterdRelays;
    }

    [ExcludeFromCodeCoverage]
    internal record Proxy(int CityIdx, MullvadRelay Server, ServiceConfig Service, ChainConfig Chain, HopConfig Hop);
}
