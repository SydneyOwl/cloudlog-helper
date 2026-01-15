using System.Text.Json.Serialization;

namespace CloudlogHelper.Models;

public struct CommonCloudlogResp
{
    [JsonPropertyName("status")] public string Status;
    [JsonPropertyName("reason")] public string Reason;
}