namespace GostGen;

using System.Collections.Generic;
using Serilog.Events;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

/// <summary>
/// Gateway configuration, which can be loaded from a yaml, json content.
/// The configuration can be used to control the behavior of the gateway.
/// </summary>
internal record GatewayConfig
{
    #region Static Fields

    /// <summary>
    /// The configuration yaml file name
    /// </summary>
    internal const string ConfigYamlFileName = "data/gateway.yaml";

    #endregion

    #region Private Fields

    #endregion

    #region Constructors

    #endregion

    #region Properties

    /// <summary>
    /// Gets the gostgen log level.
    /// </summary>
    public LogEventLevel GeneratorLogLevel { get; set; } = LogEventLevel.Information;

    /// <summary>
    /// Gets a value indicating whether gostgen should always re-generate the servers.
    /// </summary>
    public bool GeneratorAlwaysGenerateServers { get; set; }

    /// <summary>
    /// Gets or sets the gost log level.
    /// </summary>
    public GostLogLevel GostLogLevel { get; set; } = GostLogLevel.warn;

    /// <summary>
    /// Gets or sets a value indicating whether metrics for gost are enabled.
    /// </summary>
    public bool GostMetricsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the users.
    /// </summary>
    /// <remarks>Defined as dictionary for features that are user specific</remarks>
    public Dictionary<string, User> Users { get; set; } = [];

    /// <summary>
    /// Gets or sets the bypasses.
    /// </summary>
    public List<string> Bypasses { get; set; } = [];

    #endregion

    #region Public Methods

    /// <summary>
    /// Loads the configuration from a file.
    /// </summary>
    /// <param name="fullFileName">Full name of the file.</param>
    /// <returns>The configuration</returns>
    public static GatewayConfig FromFile(string fullFileName)
    {
        return FromText(File.ReadAllText(fullFileName));
    }

    /// <summary>
    /// Loads the configuration from a <see cref="string"/>.
    /// </summary>
    /// <param name="content">The yaml string.</param>
    /// <returns>The configuration</returns>
    public static GatewayConfig FromText(string content)
    {
        var config = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .Build()
            .Deserialize<GatewayConfig>(content);

        return config;
    }

    /// <summary>
    /// Converts the configuration into a yaml <see cref="string"/>.
    /// </summary>
    /// <returns>The yaml string</returns>
    public string ToYaml()
    {
        return new SerializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .Build()
            .Serialize(this);
    }

    /// <summary>
    /// Converts the configuration into a json <see cref="string"/>.
    /// </summary>
    /// <returns>The yaml string</returns>
    public string ToJson()
    {
        return new SerializerBuilder()
            .JsonCompatible()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .Build()
            .Serialize(this);
    }

    /// <summary>
    /// Validates the config.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    /// <returns>If the validation was successful or failed</returns>
    public bool Validate(out string? errorMessage)
    {
        errorMessage = null;
        if (Bypasses.Count > 0 && Bypasses.Any(string.IsNullOrWhiteSpace))
            errorMessage= "Bypasses cannot contain empty or whitespace entries";

        if (!Users.Any())
            errorMessage= "At least 1 user has to be defined";

        if (Users.Any(u => string.IsNullOrWhiteSpace(u.Key) || Users.Any(p => string.IsNullOrWhiteSpace(p.Value.Password))))
            errorMessage= "Every user requires a username and password";
        
        return errorMessage == null;
    }

    #endregion

    #region Private Methods

    #endregion
}

/// <summary>
/// User configuration, with user specific info.
/// </summary>
public record User
{
    /// <summary>
    /// Gets or sets the password.
    /// </summary>
    public string Password { get; set; } = null!;

    /// <summary>
    /// Gets or sets a value indicating whether the user has access to mullvad proxies.
    /// </summary>
    public bool HasMullvadProxyAccess { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the user has access to the internal proxy.
    /// </summary>
    public bool HasInternalProxyAccess { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user has access to the metrics server.
    /// </summary>
    public bool HasMetricsAccess { get; set; }
}

/// <summary>
/// Gost log levels see: https://gost.run/en/tutorials/log/
/// </summary>
public enum GostLogLevel
{
    /// <summary>
    /// More information than debug level output, useful for development debugging.
    /// </summary>
    trace,

    /// <summary>
    /// Output more information than info level, used to locate problems during development or use.
    /// </summary>
    debug,

    /// <summary>
    /// general infos.
    /// </summary>
    info,

    /// <summary>
    /// Warnings that you should be noticed.
    /// </summary>
    warn,

    /// <summary>
    /// General errors, the program runs normally
    /// </summary>
    error,

    /// <summary>
    /// The program will exit when this level of logging is output.
    /// </summary>
    fatal
}