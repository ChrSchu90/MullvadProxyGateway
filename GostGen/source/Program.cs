namespace GostGen;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

/// <summary>
/// App entry point
/// </summary>
public class Program
{
    private const string ExportCsvFile = "data/proxies.csv";
    private const string ExportJsonFile = "data/proxies.json";
    private const string AutherMetricsGroup = "auther-metrics";
    private const string AutherInternalGroup = "auther-internal";
    private const string AutherMullvadGroup = "auther-mullvad";
    private const string BypassMullvadGroup = "bypass-mullvad";
    private const string ServiceLocalName = "service-local";
    private const string SocksType = "socks5";
    private const string NetworkProtocol = "tcp";

    private const int MetricsPort = 9100;
    private const int ProxyPortLocal = 1080;
    private const int ProxyPortCitiesStart = 2000;
    private const int ProxyPortCitiesEnd = 3000;
    private const int ProxiesServersPerCity = 9;
    private const int ProxyPortsPerCity = ProxiesServersPerCity + 1;
    private const int PoolSelectorMaxFails = 1;
    private const string PoolSelectorFailTimeout = "10s";
    private const SelectorStrategy PoolSelectorStrategy = SelectorStrategy.round;

    private static readonly string InputInterfaceName = GetDefaultInterface();
    private static readonly Regex AddressProtRegex = new(@":(?<port>\d+)$", RegexOptions.Compiled);

    private static LoggingLevelSwitch _logLevelSwitch = null!;
    private static GatewayConfig _gatewayConfig = null!;

    /// <summary>
    /// Defines the entry point of the application.
    /// </summary>
    /// <param name="args">The arguments.</param>
    public static async Task Main(string[] args)
    {
        try
        {
            InitLogging();

            Log.Information($"Starting GOST config generator v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}");

            var gatewayConfig = LoadGatewayConfig()!;
            if (gatewayConfig == null!) return;
            _gatewayConfig = gatewayConfig;
            _logLevelSwitch.MinimumLevel = _gatewayConfig.GeneratorLogLevel;

            var gostConfig = await GetGostConfigAsync().ConfigureAwait(false);
            if (gostConfig == null) return;

            var cfgChanged = await UpdateGostLoggingAsync(gostConfig);
            cfgChanged |= await UpdateUsersAsync(gostConfig);
            cfgChanged |= await UpdateBypassesAsync(gostConfig);
            cfgChanged |= await UpdateMetricsServerAsync(gostConfig);
            cfgChanged |= await UpdateLocalProxyAsync(gostConfig);
            cfgChanged |= await UpdateGostServersAsync(gostConfig);

            if (cfgChanged)
                _ = await SaveGostConfigAsync(gostConfig).ConfigureAwait(false);
            else
                Log.Information("GOST config is already up to date");
        }
        catch (Exception err)
        {
            Log.Fatal(err, "Unhandled error");
        }

#if DEBUG
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
#endif
    }

    private static void InitLogging()
    {
        // Force english exception messages
        CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

        // Init logging
        _logLevelSwitch = new LoggingLevelSwitch { MinimumLevel = LogEventLevel.Information };
        Log.Logger = new LoggerConfiguration().MinimumLevel.ControlledBy(_logLevelSwitch)
            .WriteTo.Console(outputTemplate: "[{Timestamp:dd.MM.yyyy HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}", theme: AnsiConsoleTheme.Sixteen, applyThemeToRedirectedOutput: true)
            .CreateLogger();
    }

    private static GatewayConfig? LoadGatewayConfig()
    {
        try
        {
            if (!File.Exists(GatewayConfig.ConfigYamlFileName) || new FileInfo(GatewayConfig.ConfigYamlFileName).Length < 1)
            {
                Log.Error($"Unable to load the gateway configuration `{GatewayConfig.ConfigYamlFileName}`!");
                return null;
            }

            var config = GatewayConfig.FromFile(GatewayConfig.ConfigYamlFileName);
            if (config.Validate(out var configError)) return config;
            Log.Error($"Gateway config is invalid ({configError})");
        }
        catch (Exception err)
        {
            Log.Fatal(err, "Error on loading gateway config");
        }

        return null;
    }

    private static async Task<GostConfig?> GetGostConfigAsync()
    {
        try
        {
            if (File.Exists(GostConfig.ConfigFile) && new FileInfo(GostConfig.ConfigFile).Length > 0)
            {
                Log.Information($"Loading config `{GostConfig.ConfigFile}`");
                return GostConfig.LoadYaml(await File.ReadAllTextAsync(GostConfig.ConfigFile).ConfigureAwait(false));
            }

            Log.Information($"Creating new config since no config could not be found");
            return new GostConfig();

        }
        catch (Exception err)
        {
            Log.Fatal(err, $"Error on loading current ghost config `{GostConfig.ConfigFile}`");
            return null;
        }
    }

    private static async Task<bool> SaveGostConfigAsync(GostConfig gostConfig)
    {
        try
        {
            // Write to temp file first and then move to target file to avoid damaged file when app is killed while writing
            const string TmpFile = GostConfig.ConfigFile + ".tmp";
            Log.Information($"Saving GOST config to `{GostConfig.ConfigFile}`");
            await File.WriteAllTextAsync(TmpFile, GostConfig.ToYaml(gostConfig)).ConfigureAwait(false);
            File.Move(TmpFile, GostConfig.ConfigFile, true);
            return true;
        }
        catch (Exception err)
        {
            Log.Fatal(err, "Error on saving GOST config");
            return false;
        }
    }

    private static async Task<IReadOnlyCollection<MullvadRelay>?> GetMullvadRelaysAsync()
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

    private static Task<bool> UpdateGostLoggingAsync(GostConfig gostConfig)
    {
        const string LogFormat = "text";
        const string LogOutput = "stdout";
        if (string.Equals(gostConfig.Log?.Level, _gatewayConfig.GostLogLevel.ToString()) &&
            string.Equals(gostConfig.Log?.Format, LogFormat) &&
            string.Equals(gostConfig.Log?.Output, LogOutput))
            return Task.FromResult(false);

        Log.Debug($"Updating GOST logging level to `{_gatewayConfig.GostLogLevel}`");
        gostConfig.Log ??= new LogConfig();
        gostConfig.Log.Level = _gatewayConfig.GostLogLevel.ToString();
        gostConfig.Log.Format = LogFormat;
        gostConfig.Log.Output = LogOutput;
        return Task.FromResult(true);
    }

    private static Task<bool> UpdateUsersAsync(GostConfig gostConfig)
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
        foreach (var configUser in _gatewayConfig.Users)
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
                    _gatewayConfig.Users.TryGetValue(groupAuth.Username, out var cfgUser) &&
                    hasAccess(cfgUser))
                    continue;

                Log.Debug($"Removing user `{groupAuth.Username}` from `{group.Name}`");
                changed |= group.Auths!.Remove(groupAuth);
            }
        }

        return Task.FromResult(changed);
    }

    private static Task<bool> UpdateBypassesAsync(GostConfig gostConfig)
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
        foreach (var configBypass in _gatewayConfig.Bypasses)
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
               _gatewayConfig.Bypasses.Contains(gostBypass, StringComparer.OrdinalIgnoreCase))
                continue;

            Log.Debug($"Removing bypass `{gostBypass}` from `{BypassMullvadGroup}`");
            changed = bypassGroup.Matchers.Remove(gostBypass);
        }

        return Task.FromResult(changed);
    }

    private static Task<bool> UpdateMetricsServerAsync(GostConfig gostConfig)
    {
        const string MetricsPath = "/metrics";
        var changed = false;
        if (_gatewayConfig.GostMetricsEnabled)
        {
            var metricsAddress = $":{MetricsPort}";
            gostConfig.Metrics ??= new();
            if (gostConfig.Metrics.Addr != metricsAddress ||
                !string.Equals(gostConfig.Metrics.Path, MetricsPath) ||
                !string.Equals(gostConfig.Metrics.Auther, AutherMetricsGroup))
            {
                Log.Debug($"Enable metrics server, use `curl -v -u user:passwd http://ip:{MetricsPort}{MetricsPath}` for tests");
                gostConfig.Metrics.Addr = metricsAddress;
                gostConfig.Metrics.Path = MetricsPath;
                gostConfig.Metrics.Auther = AutherMetricsGroup;
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

    private static Task<bool> UpdateLocalProxyAsync(GostConfig gostConfig)
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
            !string.Equals(service.Interface, InputInterfaceName) ||
            !string.Equals(service.Listener?.Type, NetworkProtocol) ||
            !string.Equals(service.Handler?.Type, SocksType) ||
            !string.Equals(service.Handler?.Auther, AutherInternalGroup))
        {
            Log.Debug($"Updating local proxy configuration `{ServiceLocalName}`");
            service.Addr = address;
            service.Interface = InputInterfaceName;
            service.Listener = new() { Type = NetworkProtocol };
            service.Handler = new() { Type = SocksType, Auther = AutherInternalGroup };
            changed = true;
        }

        return Task.FromResult(changed);
    }

    private static async Task<bool> UpdateGostServersAsync(GostConfig gostConfig)
    {
        if (!_gatewayConfig.GeneratorAlwaysGenerateServers && gostConfig.Services?.Any() == true && gostConfig.Chains?.Any() == true)
        {
            Log.Information("Skip update of GOST servers");
            return false;
        }

        var relays = await GetMullvadRelaysAsync().ConfigureAwait(false);
        if (relays?.Any() != true) return false;

        var cfgChanged = false;
        Log.Information("Start to create/update GOST servers");
        ICollection<Proxy> proxies = new List<Proxy>();
        var countries = relays.Where(r => !string.IsNullOrWhiteSpace(r.CountryName)).OrderBy(r => r.CountryName).GroupBy(r => r.CountryName).ToArray();
        foreach (var country in countries)
        {
            var cities = country.Where(r => !string.IsNullOrWhiteSpace(r.CityName)).OrderBy(r => r.CityName).GroupBy(r => r.CityName).Where(g => g.Any()).ToArray();
            Log.Verbose($"Found {cities.Length} cities in {country.Key}");
            foreach (var city in cities)
            {
                var servers = city.Where(s => !string.IsNullOrWhiteSpace(s.SocksName)).OrderBy(c => c.Hostname).ToArray();
                if (!servers.Any()) continue;
                cfgChanged |= CreateOrUpdateProxy(gostConfig, city.First().CountryCode!, city.First().CityCode!, servers, ref proxies);
            }
        }

        if (cfgChanged || !File.Exists(ExportCsvFile) || new FileInfo(ExportCsvFile).Length < 1)
            ExportProxyCsv(proxies);
        if (cfgChanged || !File.Exists(ExportJsonFile) || new FileInfo(ExportJsonFile).Length < 1)
            ExportProxyJson(proxies);

        return cfgChanged;
    }

    private static bool CreateOrUpdateProxy(GostConfig gostConfig, string countryCode, string cityCode, MullvadRelay[] servers, ref ICollection<Proxy> proxies)
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
            !string.Equals(poolService.Interface, InputInterfaceName) ||
            !string.Equals(poolService.Listener?.Type, NetworkProtocol) ||
            !string.Equals(poolService.Handler?.Type, SocksType) ||
            !string.Equals(poolService.Handler?.Auther, AutherMullvadGroup) ||
            !string.Equals(poolService.Handler?.Chain, chainPoolName))
        {
            Log.Debug($"Updating proxy pool `{servicePoolName}`");
            poolService.Addr = poolServiceAddress;
            poolService.Interface = InputInterfaceName;
            poolService.Listener = new() { Type = NetworkProtocol };
            poolService.Handler = new() { Type = SocksType, Auther = AutherMullvadGroup, Chain = chainPoolName };
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
                !string.Equals(cityService.Interface, InputInterfaceName) ||
                !string.Equals(cityService.Listener?.Type, NetworkProtocol) ||
                !string.Equals(cityService.Handler?.Type, SocksType) ||
                !string.Equals(cityService.Handler?.Auther, AutherMullvadGroup) ||
                !string.Equals(cityService.Handler?.Chain, chainCityName))
            {
                Log.Debug($"Updating proxy city `{servicePoolName}`");
                cityService.Addr = cityAddress;
                cityService.Interface = InputInterfaceName;
                cityService.Listener = new() { Type = NetworkProtocol };
                cityService.Handler = new() { Type = SocksType, Auther = AutherMullvadGroup, Chain = chainCityName };
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
            if (!string.Equals(node.Bypass, BypassMullvadGroup) ||
                !string.Equals(node.Addr, svrAddress) ||
                !string.Equals(node.Connector?.Type, SocksType) ||
                !string.Equals(node.Dialer?.Type, NetworkProtocol))
            {
                Log.Debug($"Updating city node `{chainCityHopNodeName}`");
                node.Bypass = BypassMullvadGroup;
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

    private static int FindNextFreeCityPortAreaStart(GostConfig gostConfig, string servicePoolName)
    {
        gostConfig.Services ??= [];

        // Reuse existing poot port
        var service = gostConfig.Services.FirstOrDefault(s => string.Equals(s.Name, servicePoolName, StringComparison.OrdinalIgnoreCase));
        if (AddressProtRegex.IsMatch(service?.Addr ?? string.Empty))
            return int.Parse(AddressProtRegex.Match(service!.Addr!).Groups["port"].Value);

        // ToDo: More efficient way to find free port area that also supports free areas between used ports (e.g. 2000-2009 used, 2010-2019 free, 2020-2029 used -> assign next pool to 2010-2019)

        // Find next free port area
        var usedPorts = gostConfig.Services.Where(s => AddressProtRegex.IsMatch(s.Addr ?? string.Empty))
            .Select(s => int.Parse(AddressProtRegex.Match(s.Addr!).Groups["port"].Value))
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

    private static string GetDefaultInterface()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
                   .Where(i =>
                       i.OperationalStatus == OperationalStatus.Up &&
                       i.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                       i.GetIPProperties().GatewayAddresses.Any(g => !Equals(g.Address, IPAddress.Any)))
                   .Select(i => i.Name)
                   .FirstOrDefault() ?? "eth0";
    }

    private static void ExportProxyCsv(ICollection<Proxy> proxies)
    {
        try
        {
            Log.Information($"Exporting proxy list to `{ExportCsvFile}`");
            var csvString = "Country,City,Location Code,Port,Target\n" + string.Join("\n",
                proxies.Select(p => $"\"{p.Server.CountryName}\"," +
                                    $"\"{p.Server.CityName}\"," +
                                    $"{p.Server.CountryCode}-{p.Server.CityCode}," +
                                    $"{AddressProtRegex.Match(p.Service.Addr!).Groups["port"]}," +
                                    $"{(p.IsPool ? "random" : p.Server.SocksName)}")) + "\n";

            File.WriteAllText(ExportCsvFile, csvString);
        }
        catch (Exception err)
        {
            Log.Fatal(err, $"Error on exporting proxy csv {ExportCsvFile}");
        }
    }

    private static void ExportProxyJson(ICollection<Proxy> proxies)
    {
        try
        {
            Log.Information($"Exporting proxy list to `{ExportJsonFile}`");
            var export = proxies.Select(p => new
            {
                Country = p.Server.CountryName,
                City = p.Server.CityName,
                LocationCode = $"{p.Server.CountryCode}-{p.Server.CityCode}",
                Port = int.Parse(AddressProtRegex.Match(p.Service.Addr!).Groups["port"].Value),
                Target = p.IsPool ? "random" : p.Server.SocksName
            });

            File.WriteAllText(ExportJsonFile, JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception err)
        {
            Log.Fatal(err, $"Error on exporting proxy json {ExportJsonFile}");
        }
    }
    
    private record Proxy(bool IsPool, MullvadRelay Server, ServiceConfig Service);
}
