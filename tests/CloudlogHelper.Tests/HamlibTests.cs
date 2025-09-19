using System.Net;
using CloudlogHelper.Resources;
using CloudlogHelper.Services;
using CloudlogHelper.Services.Interfaces;
using Xunit.Abstractions;

namespace CloudlogHelper.Tests;

public class HamlibTests : IClassFixture<HamlibFixture>
{
    private readonly IRigService _rigctldService;

    public HamlibTests(HamlibFixture fixture)
    {
        _rigctldService = fixture.RigctldService;
    }

    [Fact]
    public async Task ParseSupportedRigList_ReturnCompleteList()
    {
        // todo
    }
    
    [Fact]
    public async Task GetAllRigInfo_ReturnDetailedRigInfo()
    {
        // todo
    }
}
public class HamlibFixture : IAsyncLifetime
{
    public IRigService RigctldService { get; private set; }
    
    public Task InitializeAsync()
    {
        RigctldService = new RigctldService();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await RigctldService.StopService(CancellationToken.None);
    }
}