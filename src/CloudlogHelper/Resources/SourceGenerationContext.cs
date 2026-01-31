using System.Collections.Generic;
using System.Text.Json.Serialization;
using CloudlogHelper.Database;
using CloudlogHelper.LogService;
using CloudlogHelper.Models;
using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Resources;

[JsonSerializable(typeof(AdifModesDatabase))]
[JsonSerializable(typeof(StationStatistics))]
[JsonSerializable(typeof(CommonCloudlogResp))]
[JsonSerializable(typeof(AdifQSOUploadCall))]
[JsonSerializable(typeof(RadioApiCallV2))]
[JsonSerializable(typeof(List<AdifModesDatabase>))]
[JsonSerializable(typeof(List<StationInfo>))]
[JsonSerializable(typeof(Dictionary<string, DXCCCountryInfo>))]
[JsonSerializable(typeof(ApplicationSettings))]

[JsonSerializable(typeof(List<ThirdPartyLogService>))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class SourceGenerationContext : JsonSerializerContext
{
    
}