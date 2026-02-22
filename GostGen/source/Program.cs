namespace GostGen;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
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
    private const string AutherMullvadGroup = "auther-mullvad";
    private const string BypassMullvadGroup = "bypass-mullvad";

    private const string ServiceLocalName = "service-local";

    private const string WireguardInterfaceName = "wg0-mullvad";
    private const string InputInterfaceName = "eth0";
    private const string SocksType = "socks5";
    private const string NetworkProtocol = "tcp";

    private const int ProxyPortLocal = 1080;
    private const int ProxyPortCitiesStart = 2000;
    private const int ProxyPortCitiesEnd = 3000;
    private const int ProxiesServersPerCity = 9;
    private const int ProxyPortsPerCity = ProxiesServersPerCity + 1;

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

            Log.Information($"Starting gost config generator v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}");

            var gatewayConfig = LoadGatewayConfig()!;
            if (gatewayConfig == null!) return;
            _gatewayConfig = gatewayConfig;
            _logLevelSwitch.MinimumLevel = _gatewayConfig.GeneratorLogLevel;

            var gostConfig = await GetGostConfigAsync().ConfigureAwait(false);
            if (gostConfig == null) return;

            var cfgChanged = await UpdateGostLoggingAsync(gostConfig);
            cfgChanged |= await UpdateUsersAsync(gostConfig);
            cfgChanged |= await UpdateBypassesAsync(gostConfig);
            cfgChanged |= await UpdateLocalProxyAsync(gostConfig);
            cfgChanged |= await UpdateGostServersAsync(gostConfig);

            if (cfgChanged)
                await SaveGostConfigAsync(gostConfig).ConfigureAwait(false);
            else
                Log.Information("Gost config is already up to date");
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
            const string tmpFile = GostConfig.ConfigFile + ".tmp";
            Log.Information($"Saving gost config to `{GostConfig.ConfigFile}`");
            await File.WriteAllTextAsync(tmpFile, GostConfig.ToYaml(gostConfig)).ConfigureAwait(false);
            File.Move(tmpFile, GostConfig.ConfigFile, true);
            return true;
        }
        catch (Exception err)
        {
            Log.Fatal(err, "Error on saving gost config");
            return false;
        }
    }

    private static async Task<IReadOnlyCollection<MullvadRelay>?> GetMullvadRelaysAsync()
    {
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
        const string LogOutput = "stderr";
        if (string.Equals(gostConfig.Log?.Level, _gatewayConfig.GostLogLevel.ToString()) &&
            string.Equals(gostConfig.Log?.Format, LogFormat) &&
            string.Equals(gostConfig.Log?.Output, LogOutput))
            return Task.FromResult(false);

        Log.Debug($"Updating gost logging level to `{_gatewayConfig.GostLogLevel}`");
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

        var mullvadGroup = gostConfig.Authers.FirstOrDefault(a => string.Equals(a.Name, AutherMullvadGroup));
        if (mullvadGroup == null)
        {
            Log.Debug($"Adding auther group `{AutherMullvadGroup}`");
            mullvadGroup = new AutherConfig { Name = AutherMullvadGroup };
            gostConfig.Authers.Add(mullvadGroup);
            changed = true;
        }

        mullvadGroup.Auths ??= [];
        foreach (var configUser in _gatewayConfig.Users)
        {
            var gostUser = mullvadGroup.Auths.FirstOrDefault(u => string.Equals(u.Username, configUser.Key));
            if (gostUser == null)
            {
                Log.Debug($"Add new user `{configUser.Key}` to `{AutherMullvadGroup}`");
                mullvadGroup.Auths.Add(new AuthConfig { Username = configUser.Key, Password = configUser.Value.Password });
                changed = true;
                continue;
            }

            if (!string.Equals(gostUser.Password, configUser.Value.Password))
            {
                Log.Debug($"Updating password for user `{configUser.Key}` to `{AutherMullvadGroup}`");
                gostUser.Password = configUser.Value.Password;
                changed = true;
            }
        }

        foreach (var gostAuth in mullvadGroup.Auths.ToArray())
        {
            if (!string.IsNullOrWhiteSpace(gostAuth.Username) &&
               _gatewayConfig.Users.ContainsKey(gostAuth.Username))
                continue;

            Log.Debug($"Removing user `{gostAuth.Username}` from `{AutherMullvadGroup}`");
            changed = mullvadGroup.Auths.Remove(gostAuth);
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
            !string.Equals(service.Handler?.Auther, AutherMullvadGroup))
        {
            Log.Debug($"Updating local proxy configuration `{ServiceLocalName}`");
            service.Addr = address;
            service.Interface = InputInterfaceName;
            service.Listener = new() { Type = NetworkProtocol };
            service.Handler = new() { Type = SocksType, Auther = AutherMullvadGroup };
            changed = true;
        }

        return Task.FromResult(changed);
    }

    private static async Task<bool> UpdateGostServersAsync(GostConfig gostConfig)
    {
        if (!_gatewayConfig.GeneratorAlwaysGenerateServers && gostConfig.Services?.Any() == true && gostConfig.Chains?.Any() == true)
        {
            Log.Information("Skip update of gost servers");
            return false;
        }

        var relays = await GetMullvadRelaysAsync().ConfigureAwait(false);
        if (relays?.Any() != true) return false;

        var cfgChanged = false;
        Log.Information("Start to create/update gost servers");
        var countries = relays.Where(r => !string.IsNullOrWhiteSpace(r.CountryName)).OrderBy(r => r.CountryName).GroupBy(r => r.CountryName).ToArray();
        Log.Debug($"Found {countries.Length} server countries");
        foreach (var country in countries)
        {
            var cities = country.Where(r => !string.IsNullOrWhiteSpace(r.CityName)).OrderBy(r => r.CityName).GroupBy(r => r.CityName).Where(g => g.Any()).ToArray();
            Log.Verbose($"Found {cities.Length} cities in {country.Key}");
            foreach (var city in cities)
            {
                var servers = city.Where(s => !string.IsNullOrWhiteSpace(s.SocksName)).OrderBy(c => c.Hostname).ToArray();
                if (!servers.Any()) continue;
                var servicePoolName = $"service-{city.First().CountryCode}-{city.First().CityCode}-pool".ToLower();
                var chainPoolName = $"chain-{city.First().CountryCode}-{city.First().CityCode}-pool".ToLower();
                var chainPoolHopName = $"hop-{chainPoolName}";
                var chainPoolHopNodeName = $"node-{chainPoolHopName}" + "-{0}";
                var poolPort = FindNextFreeCityPortAreaStart(gostConfig, servicePoolName);
                if (poolPort < 1) continue;
                cfgChanged |= CreateOrUpdateProxy(gostConfig, servers, servicePoolName, chainPoolName, chainPoolHopName, chainPoolHopNodeName, poolPort);
                
                var serverCnt = Math.Min(servers.Length, ProxiesServersPerCity);
                for (var i = 1; i <= serverCnt; i++)
                {
                    var server = servers[i - 1];
                    var serviceCityName = $"service-{server.CountryCode}-{server.CityCode}-{i}".ToLower();
                    var chainCityName = $"chain-{server.CountryCode}-{server.CityCode}-{i}".ToLower();
                    var chainCityHopName = $"hop-{chainCityName}";
                    var chainCityHopNodeName = $"node-{chainCityHopName}";
                    var port = poolPort + i;
                    cfgChanged |= CreateOrUpdateProxy(gostConfig, [server], serviceCityName, chainCityName, chainCityHopName, chainCityHopNodeName, port);
                }
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
        var lastPort = usedPorts.Any() ? usedPorts.Max() : ProxyPortCitiesStart;
        if (lastPort + 1 % ProxyPortsPerCity != 0) lastPort = (lastPort / ProxyPortsPerCity + 1) * ProxyPortsPerCity; // Round up to next multiple of ProxyPortsPerCity (10)
        lastPort = Math.Max(lastPort, ProxyPortCitiesStart);    // Limit to start of city proxy port area
        if (lastPort + ProxyPortsPerCity < ProxyPortCitiesEnd)  // Limit to end of city proxy port area
            return lastPort;

        Log.Error($"Unable to find free port area for city pool `{servicePoolName}` since last used port `{lastPort}` is to close to end of city proxy port area ({ProxyPortCitiesStart}-{ProxyPortCitiesEnd})");
        return -1;
    }

    private static bool CreateOrUpdateProxy(GostConfig gostConfig, IEnumerable<MullvadRelay> servers, string servicePoolName, string chainPoolName, string chainHopName, string chainHopNodeName, int port)
    {
        var cfgChanged = false;
        gostConfig.Services ??= [];
        var service = gostConfig.Services.FirstOrDefault(s => string.Equals(s.Name, servicePoolName, StringComparison.OrdinalIgnoreCase));
        if (service == null)
        {
            Log.Debug($"Adding proxy pool `{servicePoolName}`");
            service = new() { Name = servicePoolName };
            gostConfig.Services.Add(service);
            cfgChanged = true;
        }

        gostConfig.Chains ??= [];
        var chain = gostConfig.Chains.FirstOrDefault(c => string.Equals(c.Name, chainPoolName, StringComparison.OrdinalIgnoreCase));
        if (chain == null)
        {
            Log.Debug($"Adding proxy pool `{chainPoolName}`");
            chain = new() { Name = chainPoolName };
            gostConfig.Chains.Add(chain);
            cfgChanged = true;
        }

        chain.Hops ??= [];
        var hop = chain.Hops.FirstOrDefault(h => string.Equals(h.Name, chainHopName, StringComparison.OrdinalIgnoreCase));
        if (hop == null)
        {
            Log.Debug($"Adding proxy hop `{chainHopName}`");
            hop = new() { Name = chainHopName };
            chain.Hops.Add(hop);
            cfgChanged = true;
        }

        var portMatch = AddressProtRegex.Match(service.Addr ?? string.Empty);
        if (!portMatch.Success || int.Parse(portMatch.Groups["port"].Value) < 1 ||
            !string.Equals(service.Interface, InputInterfaceName) ||
            !string.Equals(service.Listener?.Type, NetworkProtocol) ||
            !string.Equals(service.Handler?.Type, SocksType) ||
            !string.Equals(service.Handler?.Auther, AutherMullvadGroup) ||
            !string.Equals(service.Handler?.Chain, chainPoolName))
        {
            Log.Debug($"Updating proxy pool `{servicePoolName}`");
            service.Addr = $":{port}";
            service.Interface = InputInterfaceName;
            service.Listener = new() { Type = NetworkProtocol };
            service.Handler = new() { Type = SocksType, Auther = AutherMullvadGroup, Chain = chainPoolName };
            cfgChanged = true;
        }

        hop.Nodes ??= [];
        var serverCnt = Math.Min(servers.Count(), ProxiesServersPerCity);
        for (var i = 1; i <= serverCnt; i++)
        {
            var server = servers.ElementAt(i - 1);
            var nodeName = string.Format(chainHopNodeName, i);
            var node = hop.Nodes.FirstOrDefault(n => string.Equals(n.Name, nodeName, StringComparison.OrdinalIgnoreCase));
            if (node == null)
            {
                Log.Debug($"Adding proxy node `{nodeName}`");
                node = new() { Name = nodeName };
                hop.Nodes.Add(node);
                cfgChanged = true;
            }

            var svrAddress = $"{server.SocksName}:{server.SocksPort}";
            if (!string.Equals(node.Bypass, BypassMullvadGroup) ||
                !string.Equals(node.Addr, svrAddress) ||
                !string.Equals(node.Connector?.Type, SocksType) ||
                !string.Equals(node.Dialer?.Type, NetworkProtocol))
            {
                Log.Debug($"Updating proxy node `{nodeName}`");
                node.Bypass = BypassMullvadGroup;
                node.Addr = svrAddress;
                node.Connector = new() { Type = SocksType };
                node.Dialer = new() { Type = NetworkProtocol };
                cfgChanged = true;
            }
        }

        return cfgChanged;
    }
}