namespace GostGen;

using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GostGen.DTO;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

/// <summary>
/// App entry point
/// </summary>
internal class Program
{
    /// <summary>
    /// Defines the entry point of the application.
    /// </summary>
    /// <param name="args">The arguments.</param>
    public static async Task Main(string[] args)
    {
        try
        {
            var logLevelSwitch = InitLogging();
            Log.Information($"Starting GOST config generator v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}");

            var gatewayConfig = LoadGatewayConfig()!;
            if (gatewayConfig == null!) return;
            logLevelSwitch.MinimumLevel = gatewayConfig.LogLevel;

            var gostConfig = await GetGostConfigAsync().ConfigureAwait(false);
            if (gostConfig == null) return;

            var cfgChanged = await GostLoggingSync.UpdateAsync(gostConfig, gatewayConfig);
            cfgChanged |= await GostUserSync.UpdateAsync(gostConfig, gatewayConfig);
            cfgChanged |= await GostBypassSync.UpdateAsync(gostConfig, gatewayConfig);
            cfgChanged |= await GostMetricsServerSync.UpdateAsync(gostConfig, gatewayConfig);
            cfgChanged |= await GostProxySync.UpdateAsync(gostConfig, gatewayConfig);

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

    internal static LoggingLevelSwitch InitLogging()
    {
        // Force english exception messages
        CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

        // Init logging
        var levelSwitch = new LoggingLevelSwitch { MinimumLevel = LogEventLevel.Information };
        Log.Logger = new LoggerConfiguration().MinimumLevel.ControlledBy(levelSwitch)
            .WriteTo.Console(outputTemplate: "[{Timestamp:dd.MM.yyyy HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}", theme: AnsiConsoleTheme.Sixteen, applyThemeToRedirectedOutput: true)
            .CreateLogger();
        return levelSwitch;
    }

    internal static GatewayConfig? LoadGatewayConfig()
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

    internal static async Task<GostConfig?> GetGostConfigAsync()
    {
        try
        {
            if (File.Exists(GostConfig.ConfigFile) && new FileInfo(GostConfig.ConfigFile).Length > 0)
            {
                Log.Information($"Loading GOST config `{GostConfig.ConfigFile}`");
                return GostConfig.LoadYaml(await File.ReadAllTextAsync(GostConfig.ConfigFile).ConfigureAwait(false));
            }

            Log.Information($"Creating new GOST config since no config could not be found at `{GostConfig.ConfigFile}`");
            return new GostConfig();

        }
        catch (Exception err)
        {
            Log.Fatal(err, $"Error on loading current GOST config `{GostConfig.ConfigFile}`");
            return null;
        }
    }

    internal static async Task<bool> SaveGostConfigAsync(GostConfig gostConfig)
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
}
