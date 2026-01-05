using System;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using CloudlogHelper.LogService.Attributes;
using Flurl.Http;

namespace CloudlogHelper.LogService;

[LogService("QRZ.com", Description = "QRZ Log Service")]
public class QRZThirdPartyLogService : ThirdPartyLogService
{
    private const string QRZQsoUploadEndpoint = "https://logbook.qrz.com/api";

    [UserInput("apikey", Description = "API Key of your logbook. Note you must have an" +
                                       " active subscription of qrz to use this function.")]
    public string ApiKey { get; set; }

    public override async Task TestConnectionAsync(CancellationToken token)
    {
        var result = await QRZQsoUploadEndpoint
            .PostUrlEncodedAsync(new
            {
                KEY = ApiKey,
                ACTION = "STATUS"
            }, cancellationToken: token).ConfigureAwait(false);
        var responseText = await result.GetStringAsync().ConfigureAwait(false);
        var nameValueCollection = HttpUtility.ParseQueryString(responseText);
        if (nameValueCollection.Get("RESULT") == "OK") return;
        throw new Exception($"QRZ Test failed: {nameValueCollection.Get("REASON")?.Trim()}");
    }

    public override async Task UploadQSOAsync(string? adif, CancellationToken token)
    {
        var result = await QRZQsoUploadEndpoint
            .PostUrlEncodedAsync(new
            {
                KEY = ApiKey,
                ACTION = "INSERT",
                ADIF = adif
            }, cancellationToken: token).ConfigureAwait(false);

        var responseText = await result.GetStringAsync().ConfigureAwait(false);
        var nameValueCollection = HttpUtility.ParseQueryString(responseText);
        if (nameValueCollection.Get("RESULT") == "OK") return;
        throw new Exception($"QRZ Test failed: {nameValueCollection.Get("REASON")?.Trim()}");
    }
}