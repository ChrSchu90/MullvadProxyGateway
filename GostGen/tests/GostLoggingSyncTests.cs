namespace GostGen.Tests;

using GostGen.DTO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog.Events;
using System.Threading.Tasks;

/// <summary>
/// Test for <see cref="GostLoggingSync"/>
/// </summary>
[TestClass]
public class GostLoggingSyncTests
{
    /// <summary>
    /// Tests for <see cref="GostLoggingSync.GetGostLogLevel"/>
    /// </summary>
    [TestMethod]
    [DataRow(LogEventLevel.Verbose, "trace", DisplayName = "Verbose")]
    [DataRow(LogEventLevel.Debug, "debug", DisplayName = "Debug")]
    [DataRow(LogEventLevel.Information, "info", DisplayName = "Information")]
    [DataRow(LogEventLevel.Warning, "warn", DisplayName = "Warning")]
    [DataRow(LogEventLevel.Error, "error", DisplayName = "Error")]
    [DataRow(LogEventLevel.Fatal, "fatal", DisplayName = "Fatal")]
    [DataRow((LogEventLevel)999, "debug", DisplayName = "Fallback")]
    public void GetGostLogLevel(LogEventLevel input, string expected)
    {
        var result = GostLoggingSync.GetGostLogLevel(input);
        Assert.AreEqual(expected, result, $"Failed to convert {nameof(LogEventLevel)} into GOST log level");
    }

    /// <summary>
    /// Tests for <see cref="GostLoggingSync.UpdateAsync"/>
    /// </summary>
    [TestMethod]
    [DataRow(LogEventLevel.Verbose, GostLoggingSync.LogFormat, GostLoggingSync.LogOutput, "trace", false, DisplayName = "Match")]
    [DataRow(LogEventLevel.Information, GostLoggingSync.LogFormat, GostLoggingSync.LogOutput, "trace", true, DisplayName = "Wrong level")]
    [DataRow(LogEventLevel.Information, GostLoggingSync.LogFormat, GostLoggingSync.LogOutput, null, true, DisplayName = "Missing level")]
    [DataRow(LogEventLevel.Debug, "abc", "abc", null, true, DisplayName = "Null level")]
    [DataRow(LogEventLevel.Debug, null, GostLoggingSync.LogOutput, "debug", true, DisplayName = "Missing format")]
    [DataRow(LogEventLevel.Debug, GostLoggingSync.LogFormat, null, "debug", true, DisplayName = "Missing output type")]
    [DataRow(LogEventLevel.Debug, null, null, null, true, DisplayName = "No config")]
    public async Task UpdateAsync(LogEventLevel logLevel, string? format, string? output, string? gostLogLevel, bool expectChange)
    {
            
        var gatewayConfig = new GatewayConfig { LogLevel = logLevel };
        var gostConfig = new GostConfig { Log = new LogConfig { Level = gostLogLevel, Format = format, Output = output } };

        var result = await GostLoggingSync.UpdateAsync(gostConfig, gatewayConfig);
        Assert.AreEqual(expectChange, result, "Log config change detection failed");
    }
}