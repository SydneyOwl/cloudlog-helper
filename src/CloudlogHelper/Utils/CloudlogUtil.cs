using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using Flurl;
using Flurl.Http;
using NLog;

namespace CloudlogHelper.Utils;

public class CloudlogUtil
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    /// <summary>
    ///     Creates a Flurl request configured with TLS validation settings.
    /// </summary>
    private static IFlurlRequest CreateTlsAwareRequest(string url, bool skipTlsValidation)
    {
        if (skipTlsValidation)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            var client = new FlurlClient(new HttpClient(handler));
            return client.Request(url)
                .WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent);
        }

        return url.WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent);
    }

    /// <summary>
    ///     Determinate whether this is a cloudlog server or not(wavelog)
    /// </summary>
    /// <returns></returns>
    public static async Task<ServerInstanceType> GetCurrentServerInstanceTypeAsync(string url, bool skipTlsValidation,
        CancellationToken token)
    {
        try
        {
            var result = await CreateTlsAwareRequest(url, skipTlsValidation)
                .AppendPathSegments(DefaultConfigs.CloudOrWaveCheckEndpoint)
                .GetStringAsync(cancellationToken: token).ConfigureAwait(false);
            var node = JsonNode.Parse(result);
            if (node is not JsonObject obj)
            {
                throw new Exception("Invalid JSON received!");
            }

            var instanceName = obj["name"]?.ToString();
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
    /// <param name="skipTlsValidation">Whether to skip TLS certificate validation.</param>
    /// <returns></returns>
    public static async Task<string> TestCloudlogConnectionAsync(string url, string key, bool skipTlsValidation,
        CancellationToken token)
    {
        var result = await CreateTlsAwareRequest(url, skipTlsValidation)
            .AppendPathSegments(DefaultConfigs.CloudlogTestAPIEndpoint, key)
            .GetStringAsync(cancellationToken: token).ConfigureAwait(false);

        if (!result.Contains("<auth>"))
        {
            // on ios seems it returns "Validrw"
            if (result == "Validrw") return string.Empty;
            if (result == "Validr") return TranslationHelper.GetString(LangKeys.MissingWriteAccess);
            if (result.Contains("Key Invalid")) return TranslationHelper.GetString(LangKeys.InvalidApiKey);
            return TranslationHelper.GetString(LangKeys.UnableToCheckApi);
        }

        if (!result.Contains("<status>Valid</status>"))
        {
            var description = TranslationHelper.GetString(LangKeys.InvalidApiKey);
            return description;
        }

        if (result.Contains("<rights>r</rights>"))
        {
            var description = TranslationHelper.GetString(LangKeys.MissingWriteAccess);
            return description;
        }

        if (!result.Contains("<rights>rw</rights>"))
        {
            var description = TranslationHelper.GetString(LangKeys.InvalidApiKey);
            return description;
        }

        return string.Empty;
    }

    /// <summary>
    ///     Get station statistics data. Including QSOs made this month, this year, etc.,
    /// </summary>
    /// <param name="url">Cloudlog url</param>
    /// <param name="key">key</param>
    /// <param name="skipTlsValidation">Whether to skip TLS certificate validation.</param>
    /// <returns></returns>
    public static async Task<StationStatistics?> GetStationStatisticsAsync(string url, string key, bool skipTlsValidation,
        CancellationToken token)
    {
        var result = await CreateTlsAwareRequest(url, skipTlsValidation)
            .AppendPathSegments(DefaultConfigs.CloudlogStationStatisticsAPIEndpoint, key)
            .GetStringAsync(cancellationToken: token).ConfigureAwait(false);
        var rawResult = JsonSerializer.Deserialize<StationStatistics>(result);
        return rawResult;
    }

    /// <summary>
    ///     Fetch station info, including callsign, grid, id, etc.,
    /// </summary>
    /// <param name="url">url</param>
    /// <param name="key">key</param>
    /// <param name="skipTlsValidation">Whether to skip TLS certificate validation.</param>
    /// <returns></returns>
    public static async Task<List<StationInfo>> GetStationInfoAsync(string url, string key, bool skipTlsValidation,
        CancellationToken token)
    {
        var result = await CreateTlsAwareRequest(url, skipTlsValidation)
            .AppendPathSegments(DefaultConfigs.CloudlogStationInfoAPIEndpoint, key)
            .GetStringAsync(cancellationToken: token).ConfigureAwait(false);
        var rawResult = JsonSerializer.Deserialize<List<StationInfo>>(result)
                        ?? new List<StationInfo>();
        return rawResult;
    }

    /// <summary>
    ///     Fetch station info, and returns result that matches provided stationid.
    /// </summary>
    /// <param name="url"></param>
    /// <param name="key"></param>
    /// <param name="stationId"></param>
    /// <param name="skipTlsValidation">Whether to skip TLS certificate validation.</param>
    /// <returns></returns>
    public static async Task<StationInfo?> GetStationInfoAsync(string url, string key, string stationId,
        bool skipTlsValidation, CancellationToken token)
    {
        var result = await GetStationInfoAsync(url, key, skipTlsValidation, token).ConfigureAwait(false);
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
    /// <param name="skipTlsValidation">Whether to skip TLS certificate validation.</param>
    /// <returns></returns>
    public static async Task<CommonCloudlogResp> UploadRigInfoAsync(string url, string key,
        RadioData data, bool skipTlsValidation, CancellationToken token)
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
        var results = await CreateTlsAwareRequest(url, skipTlsValidation)
            .AppendPathSegments(DefaultConfigs.CloudlogRadioAPICallV2Endpoint)
            .PostStringAsync(JsonSerializer.Serialize(payloadI), cancellationToken: token)
            .ReceiveString().ConfigureAwait(false);
        return JsonSerializer.Deserialize<CommonCloudlogResp>(results);
    }

    public static async Task<CommonCloudlogResp> UploadAdifLogAsync(string url, string key, string profileId,
        string adifLog, bool skipTlsValidation, CancellationToken token)
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
            var results = await CreateTlsAwareRequest(url, skipTlsValidation)
                .AppendPathSegments(DefaultConfigs.CloudlogQSOAPIEndpoint)
                .PostStringAsync(JsonSerializer.Serialize(payloadI), cancellationToken: token)
                .ReceiveString().ConfigureAwait(false);
            return JsonSerializer.Deserialize<CommonCloudlogResp>(results);
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