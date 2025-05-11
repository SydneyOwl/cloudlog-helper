using Newtonsoft.Json;

namespace CloudlogHelper.Models;

public struct AdifQSOUploadCall
{
    /// <summary>
    ///     Apikey of cloudlog.
    /// </summary>
    [JsonProperty("key")]
    public string Key { get; set; }
    
    /// <summary>
    ///     Profile id of cloudlog.
    /// </summary>
    [JsonProperty("station_profile_id")]
    public string StationProfileId { get; set; }

    /// <summary>
    ///     Type of the log.
    /// </summary>
    [JsonProperty("type")]
    public string Type { get; set; }
    
    /// <summary>
    ///     Log string.
    /// </summary>
    [JsonProperty("string")]
    public string LogString { get; set; }
}