using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.LogService.Attributes;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using Flurl.Http;

namespace CloudlogHelper.LogService;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
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
        var reqJson = new JsonObject();
        reqJson.Add("key", ApiKey);
        if (adif is not null)
        {
            reqJson.Add("adif", adif);
            reqJson.Add("app", DefaultConfigs.DefaultApplicationName);
        }

        var result = await HamCQQsoUploadEndpoint
            .AllowHttpStatus(406)
            .WithHeader("Content-Type", "application/json")
            .PostStringAsync(reqJson.ToString(), cancellationToken: token).ConfigureAwait(false);
        var responseText = await result.GetStringAsync().ConfigureAwait(false);
        var code = result.StatusCode;
        if (responseText == "Pass") return;
        
        var node = JsonNode.Parse(responseText);
        if (node is JsonObject obj)
        {
            throw new Exception($"HamCQ Error: {TranslationHelper.GetString(LangKeys.invalidapikey)} ({obj["message"]})");
        }

        throw new Exception($"HamCQ Error: {TranslationHelper.GetString(LangKeys.invalidapikey)}({responseText})");
    }
}