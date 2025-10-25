using CloudlogHelper.Services;
using Newtonsoft.Json;
using Xunit.Abstractions;

namespace CloudlogHelper.Tests;

public class FLRigTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public FLRigTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact(Skip = "CI")]
    public async Task GetFlrigVersion_ReturnCorrectVersion()
    {
        var flRigService = new FLRigService();
        var serviceVersion = await flRigService.GetServiceVersion("127.0.0.1", "12345");
        _testOutputHelper.WriteLine(serviceVersion);
    }


    [Fact(Skip = "CI")]
    public async Task GetFlrigRigInfo_ReturnCorrectResult()
    {
        var flRigService = new FLRigService();
        var serviceVersion =
            await flRigService.GetAllRigInfo(false, false, CancellationToken.None, "127.0.0.1", "12345");
        _testOutputHelper.WriteLine(JsonConvert.SerializeObject(serviceVersion));
    }
}