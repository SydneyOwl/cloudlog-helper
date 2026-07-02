using System.Text.Json.Serialization;

namespace CloudlogHelper.Models;

public class DxccInfoRecord
{
    [JsonPropertyName("cc")]
    public string? Cc { get; set; }

    [JsonPropertyName("ccc")]
    public string? Ccc { get; set; }

    [JsonPropertyName("flag")]
    public string? FlagImg { get; set; }
}
