namespace GostGen;

using Serilog;
using Serilog.Core;
using Serilog.Sinks.SystemConsole.Themes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Events;

/// <summary>
/// App entry point
/// </summary>
public class Program
{
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
            var gatewayConfig = LoadGatewayConfig()!;
            if (gatewayConfig == null!) return;
            _gatewayConfig = gatewayConfig;

            Log.Information($"Starting gost config generator v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}");

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
            GatewayConfig? config = null;
            var envConfig = Environment.GetEnvironmentVariable(GatewayConfig.ConfigEnvVarName);
            if (envConfig != null)
            {
                Log.Information($"Loading gateway config from Environment Variable `{GatewayConfig.ConfigEnvVarName}`");
                config = GatewayConfig.FromText(envConfig);
            }
            else if (File.Exists(GatewayConfig.ConfigYamlFileName))
            {
                Log.Information($"Loading gateway config from YAML file `{GatewayConfig.ConfigYamlFileName}`");
                config = GatewayConfig.FromFile(GatewayConfig.ConfigYamlFileName);
            }
            else if (File.Exists(GatewayConfig.ConfigJsonFileName))
            {
                Log.Information($"Loading gateway config from JSON file `{GatewayConfig.ConfigJsonFileName}`");
                config = GatewayConfig.FromFile(GatewayConfig.ConfigJsonFileName);
            }

            if (config == null)
            {
                Log.Error($"Unable to load a gateway configuration, define the config as Environment Variable `{GatewayConfig.ConfigEnvVarName}`, as YAML file `{GatewayConfig.ConfigYamlFileName}` or JSON file `{GatewayConfig.ConfigJsonFileName}`");
                return null;
            }

            if (config.Validate(out var configError)) return config;
            Log.Error($"Gateway config is invalid ({configError})");
            return null;
        }
        catch (Exception err)
        {
            Log.Fatal(err, "Error on loading gateway config");
            return null;
        }
    }

    private static async Task<GostConfig?> GetGostConfigAsync()
    {
        try
        {
            if (File.Exists(GostConfig.ConfigFile))
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
            Log.Information($"Downloading Mullvad relays from {ApiUrl}", ApiUrl);
            using var httpClient = new HttpClient();
            var json = await httpClient.GetStringAsync(ApiUrl).ConfigureAwait(false);
            var relays = JsonSerializer.Deserialize<List<MullvadRelay>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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
        {
            Log.Information("Gost logging config is already up to date");
            return Task.FromResult(false);
        }

        Log.Information("Updating gost logging config");
        gostConfig.Log ??= new LogConfig();
        gostConfig.Log.Level = _gatewayConfig.GostLogLevel.ToString();
        gostConfig.Log.Format = LogFormat;
        gostConfig.Log.Output = LogOutput;
        return Task.FromResult(true);
    }

    private static Task<bool> UpdateUsersAsync(GostConfig gostConfig)
    {
        return Task.FromResult(false);
        // ToDO: Update/Generate proxy users
    }

    private static Task<bool> UpdateBypassesAsync(GostConfig gostConfig)
    {
        return Task.FromResult(false);
        // ToDO: Update/Generate bypasses
    }

    private static Task<bool> UpdateLocalProxyAsync(GostConfig gostConfig)
    {
        return Task.FromResult(false);
        // ToDO: Update/Generate local proxy
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
        Log.Verbose($"Found {countries.Length} server countries");
        foreach (var country in countries)
        {
            var cities = country.Where(r => !string.IsNullOrWhiteSpace(r.CityName)).OrderBy(r => r.CityName).GroupBy(r => r.CityName).ToArray();
            Log.Verbose($"Found {cities.Length} cities in {country.Key}");
            foreach (var city in cities)
            {
                var servers = city.Where(s => !string.IsNullOrWhiteSpace(s.SocksName)).OrderBy(c => c.Hostname);

            }
        }

        return cfgChanged;
    }
}