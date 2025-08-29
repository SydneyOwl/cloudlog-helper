using Newtonsoft.Json;

namespace CloudlogHelper.Models;

public struct CommonCloudlogResp
{
    [JsonProperty("status")] public string Status;
    [JsonProperty("reason")] public string Reason;
}