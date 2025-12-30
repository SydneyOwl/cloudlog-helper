using CloudlogHelper.Enums;
using CloudlogHelper.Models;
using CloudlogHelper.Services;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using NLog;
using NLog.Config;
using NLog.Targets;
using Xunit.Abstractions;

namespace CloudlogHelper.Tests;

public class CLHServerServiceTests
{
    // private readonly ITestOutputHelper _testOutputHelper;
    //
    private IApplicationSettingsService  _appSettingsService;

    public CLHServerServiceTests()
    {
        // _testOutputHelper = testOutputHelper;
        var y  = new ApplicationSettingsService();
        y.InjectMockSettings(new ApplicationSettings
        {
            InstanceName = "test",
            CLHServerSettings = new CLHServerSettings
            {
                IsEnabled = true,
                ServerHost = "127.0.0.1",
                ServerPort = 7410,
                ServerKey = "djasuchy174h7fd3gh",
                UseTLS = true
            },
        });
        _appSettingsService = y;
    }

    [Fact]
    public async Task TestConnectToServer_ReturnsCorrectResult()
    { 
        var target = new MemoryTarget { Layout = "${message}" };

        var config = new LoggingConfiguration();

        config.AddRuleForAllLevels(target);
        LogManager.Configuration = config; // <-- assign here
        LogManager.GetCurrentClassLogger().Info("Hello, World!");
        
        var a = new CLHServerService(_appSettingsService);
        await a.ReconnectAsync();
        
        // await a.DisconnectAsync();
        await Task.Delay(5000000);
    }
}
