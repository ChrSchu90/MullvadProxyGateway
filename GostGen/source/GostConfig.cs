namespace GostGen;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

/// <summary>
/// Gost (https://github.com/go-gost/gost) configuration serialization structures.
/// </summary>
internal record GostConfig
{
    /// <summary>
    /// The configuration environment variable name
    /// </summary>
    internal const string ConfigFile = "data/gost.yaml";

    [YamlMember(Alias = "log")]
    public LogConfig? Log { get; set; }

    [YamlMember(Alias = "metrics")]
    public MetricsConfig? Metrics { get; set; }

    [YamlMember(Alias = "authers")]
    public List<AutherConfig>? Authers { get; set; }

    [YamlMember(Alias = "bypasses")]
    public List<BypassConfig>? Bypasses { get; set; }

    [YamlMember(Alias = "services")]
    public List<ServiceConfig>? Services { get; set; }

    [YamlMember(Alias = "chains")]
    public List<ChainConfig>? Chains { get; set; }

    //[YamlMember(Alias = "hops")]
    //public List<HopConfig> Hops? { get; set; }

    //[YamlMember(Alias = "admissions")]
    //public List<AdmissionConfig>? Admissions { get; set; }

    //[YamlMember(Alias = "resolvers")]
    //public List<ResolverConfig>? Resolvers { get; set; }

    //[YamlMember(Alias = "hosts")]
    //public List<HostsConfig>? Hosts { get; set; }

    //[YamlMember(Alias = "ingresses")]
    //public List<IngressConfig>? Ingresses { get; set; }

    //[YamlMember(Alias = "routers")]
    //public List<RouterConfig>? Routers { get; set; }

    //[YamlMember(Alias = "sds")]
    //public List<SDConfig>? SDs { get; set; }

    //[YamlMember(Alias = "recorders")]
    //public List<RecorderConfig>? Recorders { get; set; }

    //[YamlMember(Alias = "limiters")]
    //public List<LimiterConfig>? Limiters { get; set; }

    //[YamlMember(Alias = "climiters")]
    //public List<LimiterConfig>? CLimiters { get; set; }

    //[YamlMember(Alias = "rlimiters")]
    //public List<LimiterConfig>? RLimiters { get; set; }

    //[YamlMember(Alias = "observers")]
    //public List<ObserverConfig>? Observers { get; set; }

    //[YamlMember(Alias = "loggers")]
    //public List<LoggerConfig>? Loggers { get; set; }

    //[YamlMember(Alias = "tls")]
    //public TLSConfig? TLS { get; set; }

    //[YamlMember(Alias = "profiling")]
    //public ProfilingConfig? Profiling { get; set; }

    //[YamlMember(Alias = "api")]
    //public ApiConfig? API { get; set; }

    /// <summary>
    /// Loads the config from the given text.
    /// </summary>
    /// <param name="text">The yaml.</param>
    /// <returns></returns>
    internal static GostConfig LoadYaml(string text)
    {
        return new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .WithTypeConverter(new GoDurationYamlTypeConverter())
            .Build()
            .Deserialize<GostConfig>(text);
    }

    /// <summary>
    /// Converts the instance into a yaml formatted text.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <returns></returns>
    internal static string ToYaml(GostConfig config)
    {
        return new SerializerBuilder()
            .WithTypeConverter(new GoDurationYamlTypeConverter())
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .DisableAliases()
            .Build()
            .Serialize(config);
    }
}

/// <summary>
/// YamlDotNet converter that parses Go-style durations (time.Duration), e.g. "500ms", "10s", "1m30s", "2h".
/// </summary>
internal sealed class GoDurationYamlTypeConverter : IYamlTypeConverter
{
    private static readonly Regex PartRegex = new(@"(?<num>[+-]?\d+(?:\.\d+)?)(?<unit>ns|us|µs|ms|s|m|h)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <inheritdoc />
    public bool Accepts(Type type) => type == typeof(TimeSpan) || type == typeof(TimeSpan?);

    /// <inheritdoc />
    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (parser.Current is Scalar scalar)
        {
            parser.MoveNext();
            var text = scalar.Value;
            if (string.IsNullOrWhiteSpace(text))
            {
                return type == typeof(TimeSpan?) ? null : TimeSpan.Zero;
            }

            return ParseGoDuration(text);
        }

        // Let YamlDotNet handle non-scalars.
        throw new YamlException(parser.Current?.Start ?? Mark.Empty, parser.Current?.End ?? Mark.Empty,
            $"Expected scalar for Go duration, got {parser.Current?.GetType().Name ?? "null"}");
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        if (value is null)
        {
            emitter.Emit(new Scalar(string.Empty));
            return;
        }

        if (value is TimeSpan ts)
        {
            // Emit as Go-like seconds if possible; otherwise fallback to TimeSpan.ToString().
            // Keep it simple and round-trip safe.
            emitter.Emit(new Scalar(ts.ToString()));
            return;
        }

        emitter.Emit(new Scalar(value.ToString() ?? string.Empty));
    }

    private static TimeSpan ParseGoDuration(string input)
    {
        input = input.Trim();
        if (input == "0") return TimeSpan.Zero;

        var totalNanoseconds = 0.0;
        var idx = 0;

        foreach (Match m in PartRegex.Matches(input))
        {
            if (!m.Success) continue;
            if (m.Index != idx)
            {
                // There is an unparsed gap -> invalid format (e.g. "10 s" or "abc")
                throw new FormatException($"Invalid Go duration: '{input}'");
            }

            idx += m.Length;

            var numStr = m.Groups["num"].Value;
            var unit = m.Groups["unit"].Value;

            if (!double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                throw new FormatException($"Invalid number in Go duration: '{input}'");

            var factorNs = unit switch
            {
                "ns" => 1.0,
                "us" => 1_000.0,
                "µs" => 1_000.0,
                "ms" => 1_000_000.0,
                "s" => 1_000_000_000.0,
                "m" => 60.0 * 1_000_000_000.0,
                "h" => 3600.0 * 1_000_000_000.0,
                _ => throw new FormatException($"Unknown unit in Go duration: '{input}'")
            };

            totalNanoseconds += num * factorNs;
        }

        if (idx != input.Length)
            throw new FormatException($"Invalid Go duration: '{input}'");

        // TimeSpan tick = 100ns
        var ticks = (long)Math.Round(totalNanoseconds / 100.0, MidpointRounding.AwayFromZero);
        return new TimeSpan(ticks);
    }
}

internal record LogConfig
{
    [YamlMember(Alias = "level")]
    public string? Level { get; set; }

    [YamlMember(Alias = "output")]
    public string? Output { get; set; }

    [YamlMember(Alias = "format")]
    public string? Format { get; set; }

    [YamlMember(Alias = "rotation")]
    public LogRotationConfig? Rotation { get; set; }
}

internal record LogRotationConfig
{
    [YamlMember(Alias = "maxSize")]
    public int? MaxSize { get; set; }

    [YamlMember(Alias = "maxAge")]
    public int? MaxAge { get; set; }

    [YamlMember(Alias = "maxBackups")]
    public int? MaxBackups { get; set; }

    [YamlMember(Alias = "localTime")]
    public bool? LocalTime { get; set; }

    [YamlMember(Alias = "compress")]
    public bool? Compress { get; set; }
}

internal record LoggerConfig
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "log")]
    public LogConfig? Log { get; set; }
}

internal record ProfilingConfig
{
    [YamlMember(Alias = "addr")]
    public string? Addr { get; set; }
}

internal record ApiConfig
{
    [YamlMember(Alias = "addr")]
    public string? Addr { get; set; }

    [YamlMember(Alias = "pathPrefix")]
    public string? PathPrefix { get; set; }

    [YamlMember(Alias = "accesslog")]
    public bool? AccessLog { get; set; }

    [YamlMember(Alias = "auth")]
    public AuthConfig? Auth { get; set; }

    [YamlMember(Alias = "auther")]
    public string? Auther { get; set; }
}

internal record MetricsConfig
{
    [YamlMember(Alias = "addr")]
    public string? Addr { get; set; }

    [YamlMember(Alias = "path")]
    public string? Path { get; set; }

    [YamlMember(Alias = "auth")]
    public AuthConfig? Auth { get; set; }

    [YamlMember(Alias = "auther")]
    public string? Auther { get; set; }
}

internal record TLSConfig
{
    [YamlMember(Alias = "certFile")]
    public string? CertFile { get; set; }

    [YamlMember(Alias = "keyFile")]
    public string? KeyFile { get; set; }

    [YamlMember(Alias = "caFile")]
    public string? CAFile { get; set; }

    [YamlMember(Alias = "secure")]
    public bool? Secure { get; set; }

    [YamlMember(Alias = "serverName")]
    public string? ServerName { get; set; }

    [YamlMember(Alias = "options")]
    public TLSOptions? Options { get; set; }

    [YamlMember(Alias = "validity")]
    public TimeSpan? Validity { get; set; }

    [YamlMember(Alias = "commonName")]
    public string? CommonName { get; set; }

    [YamlMember(Alias = "organization")]
    public string? Organization { get; set; }
}

internal record TLSOptions
{
    [YamlMember(Alias = "minVersion")]
    public string? MinVersion { get; set; }

    [YamlMember(Alias = "maxVersion")]
    public string? MaxVersion { get; set; }

    [YamlMember(Alias = "cipherSuites")]
    public List<string>? CipherSuites { get; set; }

    [YamlMember(Alias = "alpn")]
    public List<string>? ALPN { get; set; }
}

internal record PluginConfig
{
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    [YamlMember(Alias = "addr")]
    public string? Addr { get; set; }

    [YamlMember(Alias = "tls")]
    public TLSConfig? TLS { get; set; }

    [YamlMember(Alias = "timeout")]
    public TimeSpan? Timeout { get; set; }

    [YamlMember(Alias = "token")]
    public string? Token { get; set; }
}

internal record AutherConfig
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "auths")]
    public List<AuthConfig>? Auths { get; set; }

    [YamlMember(Alias = "reload")]
    public TimeSpan? Reload { get; set; }

    [YamlMember(Alias = "file")]
    public FileLoader? File { get; set; }

    [YamlMember(Alias = "redis")]
    public RedisLoader? Redis { get; set; }

    [YamlMember(Alias = "http")]
    public HTTPLoader? HTTP { get; set; }

    [YamlMember(Alias = "plugin")]
    public PluginConfig? Plugin { get; set; }
}

internal record AuthConfig
{
    [YamlMember(Alias = "username")]
    public string? Username { get; set; }

    [YamlMember(Alias = "password")]
    public string? Password { get; set; }

    [YamlMember(Alias = "file")]
    public string? File { get; set; }
}

internal record SelectorConfig
{
    [YamlMember(Alias = "strategy")]
    public SelectorStrategy? Strategy { get; set; }

    [YamlMember(Alias = "maxFails")]
    public int? MaxFails { get; set; }

    [YamlMember(Alias = "failTimeout")]
    public string? FailTimeout { get; set; }
}

internal enum SelectorStrategy
{
    /// <summary>
    /// Round-robin
    /// </summary>
    round,

    /// <summary>
    /// Random
    /// </summary>
    rand,

    /// <summary>
    /// top-down fifo
    /// </summary>
    fifo,

    /// <summary>
    /// Based on a specific hash value (client IP or destination address)
    /// </summary>
    hash,
}

internal record AdmissionConfig
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "reverse")]
    public bool? Reverse { get; set; }

    [YamlMember(Alias = "whitelist")]
    public bool? Whitelist { get; set; }

    [YamlMember(Alias = "matchers")]
    public List<string>? Matchers { get; set; }

    [YamlMember(Alias = "reload")]
    public TimeSpan? Reload { get; set; }

    [YamlMember(Alias = "file")]
    public FileLoader? File { get; set; }

    [YamlMember(Alias = "redis")]
    public RedisLoader? Redis { get; set; }

    [YamlMember(Alias = "http")]
    public HTTPLoader? HTTP { get; set; }

    [YamlMember(Alias = "plugin")]
    public PluginConfig? Plugin { get; set; }
}

internal record BypassConfig
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "reverse")]
    public bool? Reverse { get; set; }

    [YamlMember(Alias = "whitelist")]
    public bool? Whitelist { get; set; }

    [YamlMember(Alias = "matchers")]
    public List<string>? Matchers { get; set; }

    [YamlMember(Alias = "reload")]
    public TimeSpan? Reload { get; set; }

    [YamlMember(Alias = "file")]
    public FileLoader? File { get; set; }

    [YamlMember(Alias = "redis")]
    public RedisLoader? Redis { get; set; }

    [YamlMember(Alias = "http")]
    public HTTPLoader? HTTP { get; set; }

    [YamlMember(Alias = "plugin")]
    public PluginConfig? Plugin { get; set; }
}

internal record FileLoader
{
    [YamlMember(Alias = "path")]
    public string? Path { get; set; }
}

internal record RedisLoader
{
    [YamlMember(Alias = "addr")]
    public string? Addr { get; set; }

    [YamlMember(Alias = "db")]
    public int? DB { get; set; }

    [YamlMember(Alias = "username")]
    public string? Username { get; set; }

    [YamlMember(Alias = "password")]
    public string? Password { get; set; }

    [YamlMember(Alias = "key")]
    public string? Key { get; set; }

    [YamlMember(Alias = "type")]
    public string? Type { get; set; }
}

internal record HTTPLoader
{
    [YamlMember(Alias = "url")]
    public string? URL { get; set; }

    [YamlMember(Alias = "timeout")]
    public TimeSpan? Timeout { get; set; }
}

internal record NameserverConfig
{
    [YamlMember(Alias = "addr")]
    public string? Addr { get; set; }

    [YamlMember(Alias = "chain")]
    public string? Chain { get; set; }

    [YamlMember(Alias = "prefer")]
    public string? Prefer { get; set; }

    [YamlMember(Alias = "clientIP")]
    public string? ClientIP { get; set; }

    [YamlMember(Alias = "hostname")]
    public string? Hostname { get; set; }

    [YamlMember(Alias = "ttl")]
    public TimeSpan? TTL { get; set; }

    [YamlMember(Alias = "timeout")]
    public TimeSpan? Timeout { get; set; }

    [YamlMember(Alias = "async")]
    public bool? Async { get; set; }

    [YamlMember(Alias = "only")]
    public string? Only { get; set; }
}

internal record ResolverConfig
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "nameservers")]
    public List<NameserverConfig>? Nameservers { get; set; }

    [YamlMember(Alias = "plugin")]
    public PluginConfig? Plugin { get; set; }
}

internal record HostMappingConfig
{
    [YamlMember(Alias = "ip")]
    public string? IP { get; set; }

    [YamlMember(Alias = "hostname")]
    public string? Hostname { get; set; }

    [YamlMember(Alias = "aliases")]
    public List<string>? Aliases { get; set; }
}

internal record HostsConfig
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "mappings")]
    public List<HostMappingConfig>? Mappings { get; set; }

    [YamlMember(Alias = "reload")]
    public TimeSpan? Reload { get; set; }

    [YamlMember(Alias = "file")]
    public FileLoader? File { get; set; }

    [YamlMember(Alias = "redis")]
    public RedisLoader? Redis { get; set; }

    [YamlMember(Alias = "http")]
    public HTTPLoader? HTTP { get; set; }

    [YamlMember(Alias = "plugin")]
    public PluginConfig? Plugin { get; set; }
}

internal record IngressRuleConfig
{
    [YamlMember(Alias = "hostname")]
    public string? Hostname { get; set; }

    [YamlMember(Alias = "endpoint")]
    public string? Endpoint { get; set; }
}

internal record IngressConfig
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "rules")]
    public List<IngressRuleConfig>? Rules { get; set; }

    [YamlMember(Alias = "reload")]
    public TimeSpan? Reload { get; set; }

    [YamlMember(Alias = "file")]
    public FileLoader? File { get; set; }

    [YamlMember(Alias = "redis")]
    public RedisLoader? Redis { get; set; }

    [YamlMember(Alias = "http")]
    public HTTPLoader? HTTP { get; set; }

    [YamlMember(Alias = "plugin")]
    public PluginConfig? Plugin { get; set; }
}

internal record SDConfig
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "plugin")]
    public PluginConfig? Plugin { get; set; }
}

internal record RouterRouteConfig
{
    [YamlMember(Alias = "net")]
    public string? Net { get; set; }

    [YamlMember(Alias = "dst")]
    public string? Dst { get; set; }

    [YamlMember(Alias = "gateway")]
    public string? Gateway { get; set; }
}

internal record RouterConfig
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "routes")]
    public List<RouterRouteConfig>? Routes { get; set; }

    [YamlMember(Alias = "reload")]
    public TimeSpan? Reload { get; set; }

    [YamlMember(Alias = "file")]
    public FileLoader? File { get; set; }

    [YamlMember(Alias = "redis")]
    public RedisLoader? Redis { get; set; }

    [YamlMember(Alias = "http")]
    public HTTPLoader? HTTP { get; set; }

    [YamlMember(Alias = "plugin")]
    public PluginConfig? Plugin { get; set; }
}

internal record RecorderConfig
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "file")]
    public FileRecorder? File { get; set; }

    [YamlMember(Alias = "tcp")]
    public TCPRecorder? TCP { get; set; }

    [YamlMember(Alias = "http")]
    public HTTPRecorder? HTTP { get; set; }

    [YamlMember(Alias = "redis")]
    public RedisRecorder? Redis { get; set; }

    [YamlMember(Alias = "plugin")]
    public PluginConfig? Plugin { get; set; }
}

internal record FileRecorder
{
    [YamlMember(Alias = "path")]
    public string? Path { get; set; }

    [YamlMember(Alias = "sep")]
    public string? Sep { get; set; }

    [YamlMember(Alias = "rotation")]
    public LogRotationConfig? Rotation { get; set; }
}

internal record TCPRecorder
{
    [YamlMember(Alias = "addr")]
    public string? Addr { get; set; }

    [YamlMember(Alias = "timeout")]
    public TimeSpan? Timeout { get; set; }
}

internal record HTTPRecorder
{
    [YamlMember(Alias = "url")]
    public string? URL { get; set; }

    [YamlMember(Alias = "timeout")]
    public TimeSpan? Timeout { get; set; }

    [YamlMember(Alias = "header")]
    public Dictionary<string, string>? Header { get; set; }
}

internal record RedisRecorder
{
    [YamlMember(Alias = "addr")]
    public string? Addr { get; set; }

    [YamlMember(Alias = "db")]
    public int? DB { get; set; }

    [YamlMember(Alias = "username")]
    public string? Username { get; set; }

    [YamlMember(Alias = "password")]
    public string? Password { get; set; }

    [YamlMember(Alias = "key")]
    public string? Key { get; set; }

    [YamlMember(Alias = "type")]
    public string? Type { get; set; }
}

internal record RecorderObject
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "record")]
    public string? Record { get; set; }

    [YamlMember(Alias = "metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}

internal record LimiterConfig
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "limits")]
    public List<string>? Limits { get; set; }

    [YamlMember(Alias = "reload")]
    public TimeSpan? Reload { get; set; }

    [YamlMember(Alias = "file")]
    public FileLoader? File { get; set; }

    [YamlMember(Alias = "redis")]
    public RedisLoader? Redis { get; set; }

    [YamlMember(Alias = "http")]
    public HTTPLoader? HTTP { get; set; }

    [YamlMember(Alias = "plugin")]
    public PluginConfig? Plugin { get; set; }
}

internal record ObserverConfig
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "plugin")]
    public PluginConfig? Plugin { get; set; }
}

internal record ListenerConfig
{
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    [YamlMember(Alias = "auther")]
    public string? Auther { get; set; }

    [YamlMember(Alias = "authers")]
    public List<string>? Authers { get; set; }

    [YamlMember(Alias = "auth")]
    public AuthConfig? Auth { get; set; }

    [YamlMember(Alias = "chain")]
    public string? Chain { get; set; }

    [YamlMember(Alias = "chainGroup")]
    public ChainGroupConfig? ChainGroup { get; set; }

    [YamlMember(Alias = "tls")]
    public TLSConfig? TLS { get; set; }

    [YamlMember(Alias = "metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}

internal record HandlerConfig
{
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    [YamlMember(Alias = "retries")]
    public int? Retries { get; set; }

    [YamlMember(Alias = "auther")]
    public string? Auther { get; set; }

    [YamlMember(Alias = "authers")]
    public List<string>? Authers { get; set; }

    [YamlMember(Alias = "auth")]
    public AuthConfig? Auth { get; set; }

    [YamlMember(Alias = "chain")]
    public string? Chain { get; set; }

    [YamlMember(Alias = "chainGroup")]
    public ChainGroupConfig? ChainGroup { get; set; }

    [YamlMember(Alias = "tls")]
    public TLSConfig? TLS { get; set; }

    [YamlMember(Alias = "limiter")]
    public string? Limiter { get; set; }

    [YamlMember(Alias = "observer")]
    public string? Observer { get; set; }

    [YamlMember(Alias = "metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}

internal record ForwarderConfig
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "hop")]
    public string? Hop { get; set; }

    [YamlMember(Alias = "selector")]
    public SelectorConfig? Selector { get; set; }

    [YamlMember(Alias = "nodes")]
    public List<ForwardNodeConfig>? Nodes { get; set; }
}

internal record ForwardNodeConfig
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "addr")]
    public string? Addr { get; set; }

    [YamlMember(Alias = "network")]
    public string? Network { get; set; }

    [YamlMember(Alias = "bypass")]
    public string? Bypass { get; set; }

    [YamlMember(Alias = "bypasses")]
    public List<string>? Bypasses { get; set; }

    [YamlMember(Alias = "protocol")]
    public string? Protocol { get; set; }

    [YamlMember(Alias = "host")]
    public string? Host { get; set; }

    [YamlMember(Alias = "path")]
    public string? Path { get; set; }

    [YamlMember(Alias = "filter")]
    public NodeFilterConfig? Filter { get; set; }

    [YamlMember(Alias = "matcher")]
    public NodeMatcherConfig? Matcher { get; set; }

    [YamlMember(Alias = "auth")]
    public AuthConfig? Auth { get; set; }

    [YamlMember(Alias = "http")]
    public HTTPNodeConfig? HTTP { get; set; }

    [YamlMember(Alias = "tls")]
    public TLSNodeConfig? TLS { get; set; }

    [YamlMember(Alias = "metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}

internal record NodeFilterConfig
{
    [YamlMember(Alias = "host")]
    public string? Host { get; set; }

    [YamlMember(Alias = "protocol")]
    public string? Protocol { get; set; }

    [YamlMember(Alias = "path")]
    public string? Path { get; set; }
}

internal record NodeMatcherConfig
{
    [YamlMember(Alias = "rule")]
    public string? Rule { get; set; }

    [YamlMember(Alias = "priority")]
    public int? Priority { get; set; }
}

internal record HTTPNodeConfig
{
    [YamlMember(Alias = "host")]
    public string? Host { get; set; }

    [YamlMember(Alias = "header")]
    public Dictionary<string, string>? Header { get; set; }

    [YamlMember(Alias = "requestHeader")]
    public Dictionary<string, string>? RequestHeader { get; set; }

    [YamlMember(Alias = "responseHeader")]
    public Dictionary<string, string>? ResponseHeader { get; set; }

    [YamlMember(Alias = "auth")]
    public AuthConfig? Auth { get; set; }
}

internal record TLSNodeConfig
{
    [YamlMember(Alias = "serverName")]
    public string? ServerName { get; set; }

    [YamlMember(Alias = "secure")]
    public bool Secure { get; set; }

    [YamlMember(Alias = "options")]
    public TLSOptions? Options { get; set; }
}

internal record DialerConfig
{
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    [YamlMember(Alias = "auth")]
    public AuthConfig? Auth { get; set; }

    [YamlMember(Alias = "tls")]
    public TLSConfig? TLS { get; set; }

    [YamlMember(Alias = "metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}

internal record ConnectorConfig
{
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    [YamlMember(Alias = "auth")]
    public AuthConfig? Auth { get; set; }

    [YamlMember(Alias = "tls")]
    public TLSConfig? TLS { get; set; }

    [YamlMember(Alias = "metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}

internal record SockOptsConfig
{
    [YamlMember(Alias = "mark")]
    public int? Mark { get; set; }
}

internal record ServiceConfig
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "addr")]
    public string? Addr { get; set; }

    [YamlMember(Alias = "interface")]
    public string? Interface { get; set; }

    [YamlMember(Alias = "sockopts")]
    public SockOptsConfig? SockOpts { get; set; }

    [YamlMember(Alias = "admission")]
    public string? Admission { get; set; }

    [YamlMember(Alias = "admissions")]
    public List<string>? Admissions { get; set; }

    [YamlMember(Alias = "bypass")]
    public string? Bypass { get; set; }

    [YamlMember(Alias = "bypasses")]
    public List<string>? Bypasses { get; set; }

    [YamlMember(Alias = "resolver")]
    public string? Resolver { get; set; }

    [YamlMember(Alias = "hosts")]
    public string? Hosts { get; set; }

    [YamlMember(Alias = "limiter")]
    public string? Limiter { get; set; }

    [YamlMember(Alias = "climiter")]
    public string? CLimiter { get; set; }

    [YamlMember(Alias = "rlimiter")]
    public string? RLimiter { get; set; }

    [YamlMember(Alias = "logger")]
    public string? Logger { get; set; }

    [YamlMember(Alias = "loggers")]
    public List<string>? Loggers { get; set; }

    [YamlMember(Alias = "observer")]
    public string? Observer { get; set; }

    [YamlMember(Alias = "recorders")]
    public List<RecorderObject>? Recorders { get; set; }

    [YamlMember(Alias = "handler")]
    public HandlerConfig? Handler { get; set; }

    [YamlMember(Alias = "listener")]
    public ListenerConfig? Listener { get; set; }

    [YamlMember(Alias = "forwarder")]
    public ForwarderConfig? Forwarder { get; set; }

    [YamlMember(Alias = "metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }

    [YamlMember(Alias = "status")]
    public ServiceStatus? Status { get; set; }
}

internal record ServiceStatus
{
    [YamlMember(Alias = "createTime")]
    public long? CreateTime { get; set; }

    [YamlMember(Alias = "state")]
    public string? State { get; set; }

    [YamlMember(Alias = "events")]
    public List<ServiceEvent>? Events { get; set; }

    [YamlMember(Alias = "stats")]
    public ServiceStats? Stats { get; set; }
}

internal record ServiceEvent
{
    [YamlMember(Alias = "time")]
    public long? Time { get; set; }

    [YamlMember(Alias = "msg")]
    public string? Msg { get; set; }
}

internal record ServiceStats
{
    [YamlMember(Alias = "totalConns")]
    public ulong? TotalConns { get; set; }

    [YamlMember(Alias = "currentConns")]
    public ulong? CurrentConns { get; set; }

    [YamlMember(Alias = "totalErrs")]
    public ulong? TotalErrs { get; set; }

    [YamlMember(Alias = "inputBytes")]
    public ulong? InputBytes { get; set; }

    [YamlMember(Alias = "outputBytes")]
    public ulong? OutputBytes { get; set; }
}

internal record ChainConfig
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "hops")]
    public List<HopConfig>? Hops { get; set; }

    [YamlMember(Alias = "metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}

internal record ChainGroupConfig
{
    [YamlMember(Alias = "chains")]
    public List<string>? Chains { get; set; }

    [YamlMember(Alias = "selector")]
    public SelectorConfig? Selector { get; set; }
}

internal record HopConfig
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "interface")]
    public string? Interface { get; set; }

    [YamlMember(Alias = "sockopts")]
    public SockOptsConfig? SockOpts { get; set; }

    [YamlMember(Alias = "selector")]
    public SelectorConfig? Selector { get; set; }

    [YamlMember(Alias = "bypass")]
    public string? Bypass { get; set; }

    [YamlMember(Alias = "bypasses")]
    public List<string>? Bypasses { get; set; }

    [YamlMember(Alias = "resolver")]
    public string? Resolver { get; set; }

    [YamlMember(Alias = "hosts")]
    public string? Hosts { get; set; }

    [YamlMember(Alias = "nodes")]
    public List<NodeConfig>? Nodes { get; set; }

    [YamlMember(Alias = "reload")]
    public TimeSpan? Reload { get; set; }

    [YamlMember(Alias = "file")]
    public FileLoader? File { get; set; }

    [YamlMember(Alias = "redis")]
    public RedisLoader? Redis { get; set; }

    [YamlMember(Alias = "http")]
    public HTTPLoader? HTTP { get; set; }

    [YamlMember(Alias = "plugin")]
    public PluginConfig? Plugin { get; set; }

    [YamlMember(Alias = "metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}

internal record NodeConfig
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "bypass")]
    public string? Bypass { get; set; }

    [YamlMember(Alias = "addr")]
    public string? Addr { get; set; }

    [YamlMember(Alias = "network")]
    public string? Network { get; set; }

    [YamlMember(Alias = "bypasses")]
    public List<string>? Bypasses { get; set; }

    [YamlMember(Alias = "resolver")]
    public string? Resolver { get; set; }

    [YamlMember(Alias = "hosts")]
    public string? Hosts { get; set; }

    [YamlMember(Alias = "connector")]
    public ConnectorConfig? Connector { get; set; }

    [YamlMember(Alias = "dialer")]
    public DialerConfig? Dialer { get; set; }

    [YamlMember(Alias = "interface")]
    public string? Interface { get; set; }

    [YamlMember(Alias = "netns")]
    public string? Netns { get; set; }

    [YamlMember(Alias = "sockopts")]
    public SockOptsConfig? SockOpts { get; set; }

    [YamlMember(Alias = "filter")]
    public NodeFilterConfig? Filter { get; set; }

    [YamlMember(Alias = "matcher")]
    public NodeMatcherConfig? Matcher { get; set; }

    [YamlMember(Alias = "http")]
    public HTTPNodeConfig? HTTP { get; set; }

    [YamlMember(Alias = "tls")]
    public TLSNodeConfig? TLS { get; set; }

    [YamlMember(Alias = "metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}