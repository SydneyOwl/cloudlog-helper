using CloudlogHelper.CLHProto;
using CloudlogHelper.Utils;
using Xunit.Abstractions;

namespace CloudlogHelper.Tests;

public class CLHServerUtilTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public CLHServerUtilTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public void TestCalcAuthKey_ReturnsCorrectResult()
    {
        var key = "testkey";
        var timestamp = 1234567890;
        
        var res = CLHServerUtil.CalcAuthKey(key, timestamp);
        Assert.Equal("dc93e9df2e2de4ff74c76528ca9f85be4d9b80ea15492ff5addd0ee359847c29", res);
    }
    
    [Fact]
    public async Task TestMessagePacking_ReturnsCorrectResult()
    {
        var message = new HandshakeRequest
        {
            Os = "Windows",
            Ver = "0.2.1",
            ClientType = "sender",
            AuthKey = "d4s1c5qa1dd",
            Timestamp = 1212121212121,
            RunId = "fsdfa"
        };

        var res = CLHServerUtil.Pack(message);
        _testOutputHelper.WriteLine(res.Length.ToString());

        var iMessage = await CLHServerUtil.ReadMsgAsync(new MemoryStream(res));
        _testOutputHelper.WriteLine(iMessage.ToString());
    }
}