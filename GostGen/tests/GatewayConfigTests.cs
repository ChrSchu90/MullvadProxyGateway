namespace GostGen.Tests;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog.Events;


/// <summary>
/// Test for <see cref="GatewayConfig"/>
/// </summary>
[TestClass]
public sealed class GatewayConfigTests
{
    [TestMethod]
    public void Deserialization()
    {
        var jsonCfg = GatewayConfig.FromText(GetTestJson());
        var yamlCfg = GatewayConfig.FromText(GetTestYaml());
        Assert.AreEqual(jsonCfg.ToYaml(), yamlCfg.ToYaml(), "Yaml to Json deserialization difference");
        Assert.AreEqual(jsonCfg.ToJson(), yamlCfg.ToJson(), "Json to Yaml deserialization difference");

        Assert.AreEqual(LogEventLevel.Debug, jsonCfg.GeneratorLogLevel, "Wrong log level inside Json");
        Assert.AreEqual(LogEventLevel.Debug, yamlCfg.GeneratorLogLevel, "Wrong log level inside Yaml");
    }

    [TestMethod]
    public void Validation()
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

    private static string GetTestYaml()
    {
        return """
               GeneratorLogLevel: Debug
               GeneratorAlwaysGenerateServers: false
               GostLogLevel: debug
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
                   "GeneratorLogLevel": "Debug",
                   "GeneratorAlwaysGenerateServers": false,
                   "GostLogLevel": "debug",
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
