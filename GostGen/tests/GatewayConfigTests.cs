namespace GostGen.Tests;

using System.IO;
using GostGen.DTO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog.Events;

/// <summary>
/// Test for <see cref="GatewayConfig"/>
/// </summary>
[TestClass]
public sealed class GatewayConfigTests
{
    [TestMethod]
    public void ConfigSerialization()
    {
        var jsonCfg = GatewayConfig.FromText(GetTestJson());
        var yamlCfg = GatewayConfig.FromText(GetTestYaml());
        Assert.AreEqual(jsonCfg.ToYaml(), yamlCfg.ToYaml(), "Yaml to Json deserialization difference");
        Assert.AreEqual(jsonCfg.ToJson(), yamlCfg.ToJson(), "Json to Yaml deserialization difference");

        Assert.AreEqual(LogEventLevel.Debug, jsonCfg.LogLevel, "Wrong log level inside Json");
        Assert.AreEqual(LogEventLevel.Debug, yamlCfg.LogLevel, "Wrong log level inside Yaml");
    }

    [TestMethod]
    public void ConfigValidation()
    {
        var cfg = GatewayConfig.FromText(GetTestJson());
        Assert.IsTrue(cfg.Validate(out var error), "Test config validation failed");
        Assert.IsNull(error, "Test config validation error message not null");

        var oldBypasses = cfg.Bypasses;
        cfg.Bypasses = [];
        Assert.IsTrue(cfg.Validate(out error), "Config validation should pass if no bypasses are defined");
        Assert.IsNull(error, "Config validation error message should be null if no bypasses are defined");
        cfg.Bypasses.Add("      ");
        Assert.IsFalse(cfg.Validate(out error), "Config validation should fail if empty bypass is defined");
        Assert.IsNotNull(error, "Config validation error message should not be null if empty bypass is defined");
        cfg.Bypasses = oldBypasses;

        var oldUsers = cfg.Users;
        cfg.Users = [];
        Assert.IsFalse(cfg.Validate(out error), "Config validation should fail if no users are defined");
        Assert.IsNotNull(error, "Config validation error message should not be null if no users are defined");
        cfg.Users.Add(" ", new());
        Assert.IsFalse(cfg.Validate(out error), "Config validation should fail for user with empty user name");
        Assert.IsNotNull(error, "Config validation error message should not be null for user with empty user name");
        cfg.Users.Clear();
        cfg.Users.Add("User1", new User { Password = string.Empty });
        Assert.IsFalse(cfg.Validate(out error), "Config validation should fail for user with empty user password");
        Assert.IsNotNull(error, "Config validation error message should not be null for user with empty user password");
        cfg.Users = oldUsers;
    }

    [TestMethod]
    public void GostGenLoading()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(GatewayConfig.ConfigYamlFileName)!);
        if(File.Exists(GatewayConfig.ConfigYamlFileName)) File.Delete(GatewayConfig.ConfigYamlFileName);
        var config = Program.LoadGatewayConfig();
        Assert.IsNull(config, "No gateway config is available, but a config has been returned");

        var testConfig = GatewayConfig.FromText(GetTestYaml());
        File.WriteAllText(GatewayConfig.ConfigYamlFileName, testConfig.ToYaml());
        config = Program.LoadGatewayConfig();
        Assert.IsNotNull(config, "Gateway config is not available, but should have been loaded from file");
        Assert.AreEqual(testConfig.ToYaml(), config.ToYaml(), "Loaded gateway config does not match");
    }

    private static string GetTestYaml()
    {
        return """
               LogLevel: Debug
               AlwaysGenerateServers: false
               Users:
                 User1:
                   Password: Password1
                 User2:
                   Password: Password2
               Bypasses:
               - 'example.com'
               - '*.example.com'
               """;
    }

    private static string GetTestJson()
    {
        return """
               {
                   "LogLevel": "Debug",
                   "AlwaysGenerateServers": false,
                   "Users": {
                       "User1": { "Password": "Password1" },
                       "User2": { "Password": "Password2" }
                   },
                   "Bypasses": [
                       "example.com",
                       "*.example.com"
                   ]
               }
               """;
    }
}
