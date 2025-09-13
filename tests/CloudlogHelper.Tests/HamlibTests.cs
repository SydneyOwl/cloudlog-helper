using System.Net;
using CloudlogHelper.Resources;
using CloudlogHelper.Services;
using CloudlogHelper.Services.Interfaces;
using Xunit.Abstractions;

namespace CloudlogHelper.Tests;

#if false
public class HamlibTests : IClassFixture<HamlibFixture>
{
    private readonly IRigctldService _rigctldService;

    public HamlibTests(HamlibFixture fixture)
    {
        _rigctldService = fixture.RigctldService;
    }

    [Fact]
    public async Task ParseSupportedRigList_ReturnCompleteList()
    {
        var (listResult, opt) = await _rigctldService.StartOnetimeRigctldAsync("--list");
        Assert.True(listResult);
        Assert.NotEmpty(_rigctldService.ParseAllModelsFromRawOutput(opt)
                .OrderBy(x => x.Model)
                .ToList());
    }
    
    [Fact]
    public async Task GetAllRigInfo_ReturnDetailedRigInfo()
    {
        var (res, des) =
            await _rigctldService.RestartRigctldBackgroundProcessAsync($"-m 1 -T {DefaultConfigs.RigctldDefaultHost} -t {DefaultConfigs.RigctldDefaultPort} -vvvvv");
        Assert.True(res);
        
        // try set SPLIT and freq
        await _rigctldService.ExecuteCommandInScheduler(DefaultConfigs.RigctldDefaultHost,
            DefaultConfigs.RigctldDefaultPort, "F 21021000", false);
        await _rigctldService.ExecuteCommandInScheduler(DefaultConfigs.RigctldDefaultHost,
            DefaultConfigs.RigctldDefaultPort, "M CW 0", false);
        await _rigctldService.ExecuteCommandInScheduler(DefaultConfigs.RigctldDefaultHost,
            DefaultConfigs.RigctldDefaultPort, "S 1 currVFO", false);
        
        var data = await _rigctldService.GetAllRigInfo(DefaultConfigs.RigctldDefaultHost, DefaultConfigs.RigctldDefaultPort,
            true, true, CancellationToken.None);
        Assert.Equal(21021000, data.FrequencyRx);
        Assert.Equal(21021000, data.FrequencyTx);
        Assert.Equal("CW", data.ModeRx);
        Assert.Equal("CW", data.ModeTx);
    }
}
public class HamlibFixture : IAsyncLifetime
{
    public IRigctldService RigctldService { get; private set; }
    
    public Task InitializeAsync()
    {
        RigctldService = new RigctldService();
        RigctldService.InitScheduler();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        RigctldService.TerminateBackgroundProcess();
        RigctldService.TerminateOnetimeProcess();
        return Task.CompletedTask;
    }
}
#endif