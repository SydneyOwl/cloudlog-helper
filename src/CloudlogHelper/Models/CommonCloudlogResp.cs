using System.Text.Json.Serialization;

namespace CloudlogHelper.Models;

public struct CommonCloudlogResp
{
    [JsonPropertyName("status")] public string Status { get; set; }
    [JsonPropertyName("reason")] public string Reason { get; set; }
}