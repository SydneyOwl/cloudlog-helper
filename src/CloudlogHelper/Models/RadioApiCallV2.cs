using Newtonsoft.Json;

namespace CloudlogHelper.Models;

// this is `v2` version of the call: https://github.com/magicbug/Cloudlog/wiki/API
public record RadioApiCallV2
{
    /// <summary>
    ///     Apikey of cloudlog.
    /// </summary>
    [JsonProperty("key")]
    public string Key { get; set; }

    /// <summary>
    ///     Rig model. (used for assigning received data)
    /// </summary>
    [JsonProperty("radio")]
    public string Radio { get; set; }

    /// <summary>
    ///     Frequency in Hz.
    /// </summary>
    [JsonProperty("frequency")]
    public long Frequency { get; set; }

    /// <summary>
    ///     Mode of the rig.
    /// </summary>
    [JsonProperty("mode")]
    public string Mode { get; set; }

    /// <summary>
    ///     Optional Rx frequency in Hz.
    /// </summary>
    [JsonProperty("frequency_rx")]
    public long? FrequencyRx { get; set; }

    /// <summary>
    ///     Optional Rx mode (not logged).
    /// </summary>
    [JsonProperty("mode_rx")]
    public string? ModeRx { get; set; }

    /// <summary>
    ///     Optional transmit power in Watts.
    /// </summary>
    [JsonProperty("power")]
    public float? Power { get; set; }

    // "prop_mode" and "sat_name"

    public override string ToString()
    {
        return
            $"{nameof(Key)}: {Key}, {nameof(Radio)}: {Radio}, {nameof(Frequency)}: {Frequency}, {nameof(Mode)}: {Mode}, {nameof(FrequencyRx)}: {FrequencyRx}, {nameof(ModeRx)}: {ModeRx}, {nameof(Power)}: {Power}";
    }
}