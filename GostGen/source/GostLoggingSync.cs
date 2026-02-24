namespace GostGen;

using GostGen.DTO;
using Serilog;
using Serilog.Events;
using System.Threading.Tasks;

/// <summary>
/// Synchronizes the <see cref="GostConfig"/> logging configuration with the <see cref="GatewayConfig"/>
/// </summary>
internal class GostLoggingSync
{
    internal const string LogFormat = "text";
    internal const string LogOutput = "stdout";
    
    /// <summary>
    /// Updates the logging inside the <see cref="GostConfig"/>.
    /// </summary>
    /// <param name="gostConfig">The GOST configuration.</param>
    /// <param name="gatewayConfig">The gateway configuration.</param>
    /// <returns><c>true</c> if the <see cref="GostConfig"/> has been changed.</returns>
    internal static Task<bool> UpdateAsync(GostConfig gostConfig, GatewayConfig gatewayConfig)
    {
        var logLevel = GetGostLogLevel(gatewayConfig.LogLevel);
        if (string.Equals(gostConfig.Log?.Level, logLevel) &&
            string.Equals(gostConfig.Log?.Format, LogFormat) &&
            string.Equals(gostConfig.Log?.Output, LogOutput))
            return Task.FromResult(false);

        Log.Debug($"Updating GOST logging level to `{logLevel}`");
        gostConfig.Log = new LogConfig
        {
            Format = LogFormat,
            Output = LogOutput,
            Level = logLevel
        };
        return Task.FromResult(true);
    }

    /// <summary>
    /// Gets the GOST log level based on the <see cref="LogEventLevel"/>.
    /// </summary>
    /// <param name="logLevel">The target log level.</param>
    /// <returns>The GOST specific log level name.</returns>
    internal static string GetGostLogLevel(LogEventLevel logLevel)
    {
        switch (logLevel)
        {
            case LogEventLevel.Verbose:
                return "trace";
            case LogEventLevel.Debug:
                return "debug";
            case LogEventLevel.Information:
                return "info";
            case LogEventLevel.Warning:
                return "warn";
            case LogEventLevel.Error:
                return "error";
            case LogEventLevel.Fatal:
                return "fatal";
            default:
                return "debug";
        }
    }
}
