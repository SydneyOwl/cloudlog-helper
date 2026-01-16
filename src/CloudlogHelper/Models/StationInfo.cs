using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace CloudlogHelper.Models;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class StationInfo
{
    /// <summary>
    ///     Id of the station.　Can be found in station info.
    /// </summary>
    [JsonPropertyName("station_id")]
    public string? StationId { get; set; }

    /// <summary>
    ///     The profile name of the station.
    /// </summary>
    [JsonPropertyName("station_profile_name")]
    public string? StationProfileName { get; set; }

    /// <summary>
    ///     Gridsquare(MaidenheadLocator) of the station.
    /// </summary>
    [JsonPropertyName("station_gridsquare")]
    public string? StationGridsquare { get; set; }

    /// <summary>
    ///     Callsign of the station.
    /// </summary>
    [JsonPropertyName("station_callsign")]
    public string? StationCallsign { get; set; }


    /// <summary>
    ///     Active status of the station.
    /// </summary>
    [JsonPropertyName("station_active")]
    public string? StationActive { get; set; }
}