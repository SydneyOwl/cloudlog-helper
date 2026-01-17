using System.Text.Json.Nodes;

namespace CloudlogHelper.Migration;

public class SettingsMigration
{
    public static string MigrateSettings_B4_0_3_0(string rawSettings)
    {
        rawSettings = rawSettings.Replace("CloudlogHelper.LogService.EqslThirdPartyLogService, CloudlogHelper","EqslThirdPartyLogService");
        rawSettings = rawSettings.Replace("CloudlogHelper.LogService.HamCQThirdPartyLogService, CloudlogHelper","HamCQThirdPartyLogService");
        rawSettings = rawSettings.Replace("CloudlogHelper.LogService.HRDLogThirdPartyLogService, CloudlogHelper","HRDLogThirdPartyLogService");
        rawSettings = rawSettings.Replace("CloudlogHelper.LogService.LoTWThirdPartyLogService, CloudlogHelper","LoTWThirdPartyLogService");
        rawSettings = rawSettings.Replace("CloudlogHelper.LogService.QRZThirdPartyLogService, CloudlogHelper","QRZThirdPartyLogService");
        
        var rootNode = JsonNode.Parse(rawSettings);
        if (rootNode is null) return string.Empty;
        var cloudlogSettingsNode = rootNode["CloudlogSettings"];
        if (cloudlogSettingsNode != null)
        {
            var cloudlogSettingsObj = cloudlogSettingsNode.AsObject();
    
            if (cloudlogSettingsObj.TryGetPropertyValue("CloudlogStationInfo", out var stationInfoNode))
            {
                if (stationInfoNode is not null)
                {
                    var stationId = stationInfoNode["station_id"].GetValue<string>();
                    cloudlogSettingsObj.Remove("CloudlogStationInfo");
                    cloudlogSettingsObj["CloudlogStationInfoId"] = JsonValue.Create(stationId);   
                }
            }
        }

        return rootNode.ToJsonString();
    }
}