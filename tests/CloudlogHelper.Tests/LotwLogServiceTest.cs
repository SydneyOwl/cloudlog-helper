using CloudlogHelper.LogService;

namespace CloudlogHelper.Tests;

public class LotwLogServiceTest
{
    [Theory(Skip = "")]
    [InlineData("<call:8>HS0FVS/M <gridsquare:0> <mode:3>FT8 <rst_sent:3>-07 <rst_rcvd:3>-07 <qso_date:8>20240626 <time_on:6>093500 <qso_date_off:8>20240626 <time_off:6>093559 <band:3>15m <freq:9>21.076340 <station_callsign:6>BG5VLI <my_gridsquare:4>OL94 <comment:10>Distance:  <eor>\n")]
    public async Task UploadTestAdif_UploadedSuccessfully(string adif)
    {
        var lotwThirdPartyLogService = new LoTWThirdPartyLogService();
        lotwThirdPartyLogService.PreInitSync();
        await lotwThirdPartyLogService.TestConnectionAsync(CancellationToken.None);
        await lotwThirdPartyLogService.UploadQSOAsync(adif,CancellationToken.None);
    }
}