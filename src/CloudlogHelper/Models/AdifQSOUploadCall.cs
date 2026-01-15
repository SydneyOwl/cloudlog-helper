using System.Text.Json.Serialization;

namespace CloudlogHelper.Models;

public struct AdifQSOUploadCall
{
    /// <summary>
    ///     Apikey of cloudlog.
    /// </summary>
    [JsonPropertyName("key")]
    public string Key { get; set; }

    /// <summary>
    ///     Profile id of cloudlog.
    /// </summary>
    [JsonPropertyName("station_profile_id")]
    public string StationProfileId { get; set; }

    /// <summary>
    ///     Type of the log.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; }

    /// <summary>
    ///     Log string.
    /// </summary>
    [JsonPropertyName("string")]
    public string LogString { get; set; }
}