using System;
using System.Threading.Tasks;
using CloudlogHelper.Resources;
using Flurl.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace CloudlogHelper.Utils;

public class HamCQUtil
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    public static async Task<string> TestHamCQConnectionAsync(string apikey)
    {
        return await UploadQSOToHamCQAsync(apikey, null);
    }

    public static async Task<string> UploadQSOToHamCQAsync(string apikey, string? adif)
    {
        try
        {
            var reqJson = new JObject { { "key", apikey } };
            if (adif is not null)
            {
                reqJson.Add("adif", adif);
                reqJson.Add("app", DefaultConfigs.DefaultApplicationName);
            }
            // ClassLogger.Trace(reqJson.ToString());
            var result = await DefaultConfigs.HamCQQsoUploadEndpoint
                .AllowHttpStatus(406)
                .WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
                .WithHeader("Content-Type", "application/json")
                .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.DefaultRequestTimeout))
                .PostStringAsync(reqJson.ToString());
            var responseText = await result.GetStringAsync();
            var code = result.StatusCode;
            ClassLogger.Debug($"Got response from `TestHamCQConnectionAsync`: {code} {responseText}");
            if (responseText == "Pass")return string.Empty;
            var res = JsonConvert.DeserializeObject<JObject>(responseText);
            if (res is null) return $"HamCQ Error: {TranslationHelper.GetString("invalidapikey")}({responseText})";
            return $"HamCQ Error: {TranslationHelper.GetString("invalidapikey")} ({res["message"]})";
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }
}