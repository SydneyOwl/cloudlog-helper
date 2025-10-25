using System;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.LogService.Attributes;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using Flurl.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CloudlogHelper.LogService;

[LogService("HamCQ", Description = "HamCQ Log Service")]
public class HamCQThirdPartyLogService : ThirdPartyLogService
{
    private const string HamCQQsoUploadEndpoint = "https://api.hamcq.cn/v1/logbook?from=gridtracker";

    [UserInput("apikey")] public string ApiKey { get; set; }

    public override Task TestConnectionAsync(CancellationToken token)
    {
        return UploadQSOAsync(null, token);
    }

    public override async Task UploadQSOAsync(string? adif, CancellationToken token)
    {
        var reqJson = new JObject { { "key", ApiKey } };
        if (adif is not null)
        {
            reqJson.Add("adif", adif);
            reqJson.Add("app", DefaultConfigs.DefaultApplicationName);
        }

        var result = await HamCQQsoUploadEndpoint
            .AllowHttpStatus(406)
            .WithHeader("Content-Type", "application/json")
            .PostStringAsync(reqJson.ToString(), cancellationToken: token);
        var responseText = await result.GetStringAsync();
        var code = result.StatusCode;
        if (responseText == "Pass") return;
        var res = JsonConvert.DeserializeObject<JObject>(responseText);
        if (res is null)
            throw new Exception($"HamCQ Error: {TranslationHelper.GetString(LangKeys.invalidapikey)}({responseText})");
        throw new Exception($"HamCQ Error: {TranslationHelper.GetString(LangKeys.invalidapikey)} ({res["message"]})");
    }
}