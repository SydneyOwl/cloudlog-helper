using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using Flurl;
using Flurl.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace CloudlogHelper.Utils;

public class CloudlogUtil
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    /// <summary>
    ///     Determinate whether this is a cloudlog server or not(wavelog)
    /// </summary>
    /// <returns></returns>
    public static async Task<ServerInstanceType> GetCurrentServerInstanceTypeAsync(string url, CancellationToken token)
    {
        try
        {
            var result = await url
                .AppendPathSegments(DefaultConfigs.CloudOrWaveCheckEndpoint)
                .WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
                .GetStringAsync(cancellationToken: token);
            var pp = JsonConvert.DeserializeObject<JObject>(result)!;
            var instanceName = pp.GetValue("name")!.ToString();
            return instanceName switch
            {
                "Cloudlog" => ServerInstanceType.Cloudlog,
                "Wavelog" => ServerInstanceType.Wavelog,
                _ => ServerInstanceType.Unknown
            };
        }
        catch (Exception ex)
        {
            ClassLogger.Warn(ex, "Error GetCurrentServerInstanceType.");
            return ServerInstanceType.Unknown;
        }
    }

    /// <summary>
    ///     Test whether cloudlog url is reachable, and the key is correct.
    /// </summary>
    /// <param name="url">URL of the cloudlog server.</param>
    /// <param name="key">APIKEY</param>
    /// <returns></returns>
    public static async Task<string> TestCloudlogConnectionAsync(string url, string key, CancellationToken token)
    {
        var result = await url
            .AppendPathSegments(DefaultConfigs.CloudlogTestAPIEndpoint, key)
            .GetStringAsync(cancellationToken: token);

        if (!result.Contains("<auth>"))
        {
            // on ios seems it returns "Validrw"
            if (result == "Validrw") return string.Empty;
            if (result == "Validr") return TranslationHelper.GetString(LangKeys.lackwriteaccess);
            if (result.Contains("Key Invalid")) return TranslationHelper.GetString(LangKeys.invalidapikey);
            return TranslationHelper.GetString(LangKeys.unable2checkapi);
        }

        if (!result.Contains("<status>Valid</status>"))
        {
            var description = TranslationHelper.GetString(LangKeys.invalidapikey);
            return description;
        }

        if (result.Contains("<rights>r</rights>"))
        {
            var description = TranslationHelper.GetString(LangKeys.lackwriteaccess);
            return description;
        }

        if (!result.Contains("<rights>rw</rights>"))
        {
            var description = TranslationHelper.GetString(LangKeys.invalidapikey);
            return description;
        }

        return string.Empty;
    }

    /// <summary>
    ///     Get station statistics data. Including QSOs made this month, this year, etc.,
    /// </summary>
    /// <param name="url">Cloudlog url</param>
    /// <param name="key">key</param>
    /// <returns></returns>
    public static async Task<StationStatistics?> GetStationStatisticsAsync(string url, string key,
        CancellationToken token)
    {
        var result = await url
            .AppendPathSegments(DefaultConfigs.CloudlogStationStatisticsAPIEndpoint, key)
            .GetStringAsync(cancellationToken: token);
        var rawResult = JsonConvert.DeserializeObject<StationStatistics>(result);
        return rawResult;
    }

    /// <summary>
    ///     Fetch station info, including callsign, grid, id, etc.,
    /// </summary>
    /// <param name="url">url</param>
    /// <param name="key">key</param>
    /// <returns></returns>
    public static async Task<List<StationInfo>> GetStationInfoAsync(string url, string key, CancellationToken token)
    {
        var result = await url
            .AppendPathSegments(DefaultConfigs.CloudlogStationInfoAPIEndpoint, key)
            .GetStringAsync(cancellationToken: token);
        var rawResult = JsonConvert.DeserializeObject<List<StationInfo>>(result)
                        ?? new List<StationInfo>();
        return rawResult;
    }

    /// <summary>
    ///     Fetch station info, and returns result that matches provided stationid.
    /// </summary>
    /// <param name="url"></param>
    /// <param name="key"></param>
    /// <param name="stationId"></param>
    /// <returns></returns>
    public static async Task<StationInfo?> GetStationInfoAsync(string url, string key, string stationId,
        CancellationToken token)
    {
        var result = await GetStationInfoAsync(url, key, token);
        if (result.Count == 0) return null;
        foreach (var stationInfo in result)
            if (stationInfo.StationId == stationId)
                return stationInfo;

        return null;
    }

    /// <summary>
    ///     Upload rig info to server.
    /// </summary>
    /// <param name="url"></param>
    /// <param name="key"></param>
    /// <param name="rigName"></param>
    /// <param name="data"></param>
    /// <returns></returns>
    public static async Task<CommonCloudlogResp> UploadRigInfoAsync(string url, string key,
        RadioData data, CancellationToken token)
    {
        var payloadI = new RadioApiCallV2
        {
            Key = key,
            Radio = data.RigName,
            Frequency = data.FrequencyTx,
            Mode = data.ModeTx,
            FrequencyRx = data.FrequencyRx,
            ModeRx = data.ModeRx,
            Power = data.Power
        };
        var results = await url
            .AppendPathSegments(DefaultConfigs.CloudlogRadioAPICallV2Endpoint)
            .PostStringAsync(JsonConvert.SerializeObject(payloadI), cancellationToken: token)
            .ReceiveString();
        return JsonConvert.DeserializeObject<CommonCloudlogResp>(results);
    }

    public static async Task<CommonCloudlogResp> UploadAdifLogAsync(string url, string key, string profileId,
        string adifLog, CancellationToken token)
    {
        try
        {
            // construct json
            var payloadI = new AdifQSOUploadCall
            {
                Key = key,
                StationProfileId = profileId,
                Type = "adif",
                LogString = adifLog
            };
            var results = await url
                .AppendPathSegments(DefaultConfigs.CloudlogQSOAPIEndpoint)
                .WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
                .PostStringAsync(JsonConvert.SerializeObject(payloadI), cancellationToken: token)
                .ReceiveString();
            return JsonConvert.DeserializeObject<CommonCloudlogResp>(results);
        }
        catch (Exception e)
        {
            return new CommonCloudlogResp
            {
                Status = "Failed",
                Reason = e.Message
            };
        }
    }

    /// <summary>
    ///     Just some tests.
    /// </summary>
    public static void Test()
    {
    }
}