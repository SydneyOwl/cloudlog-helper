using System.Text.Json.Serialization;

namespace CloudlogHelper.Models;

public class DXCCCountryInfo
{
    [JsonPropertyName("cc")]
    public string Alpha2Code { get; set; }
    
    [JsonPropertyName("ccc")]
    public string Alpha3Code { get; set; }
    
    [JsonPropertyName("flag")]
    public string FlagPngName { get; set; }
}