using CloudlogHelper.Services;
using Newtonsoft.Json;
using Xunit.Abstractions;

namespace CloudlogHelper.Tests;

public class OmniRigTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public OmniRigTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

#if WINDOWS
    [Fact(Skip = "CI")]
    public async Task GetOmniRigVersion_ReturnCorrectVersion()
    {
        var omniRigService = new OmniRigService();
        
        await omniRigService.StartService(CancellationToken.None, 1);
        
        
        _testOutputHelper.WriteLine(omniRigService.IsServiceRunning().ToString());
        
        var serviceVersion = await omniRigService.GetServiceVersion();
        _testOutputHelper.WriteLine(serviceVersion);
        
        await omniRigService.StopService(CancellationToken.None);
        
        _testOutputHelper.WriteLine(omniRigService.IsServiceRunning().ToString());
    }
    
    [Fact(Skip = "CI")]
    public async Task GetRawOmniRigVersion_ReturnCorrec()
    {
        var omniRigService = new OmniRigService();
        
        await omniRigService.StartService(CancellationToken.None, 1);
        
        _testOutputHelper.WriteLine((await omniRigService.GetAllRigInfo(false,false,CancellationToken.None)).ToString());

        // var raw = omniRigService.GetRawRig();
        //
        // _testOutputHelper.WriteLine(raw.RigType.ToString());
        // _testOutputHelper.WriteLine(raw.Freq.ToString());
        // _testOutputHelper.WriteLine(raw.Mode.ToString());
        // _testOutputHelper.WriteLine(raw.Status.ToString());
        
        
        await omniRigService.StopService(CancellationToken.None);
    }
#endif

}