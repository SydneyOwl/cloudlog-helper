using System.Text.Json.Nodes;

namespace CloudlogHelper.Migration;

public class SettingsMigration
{
    public static string MigrateSettings_B4_0_3_0(string rawSettings)
    {
        try
        {
            rawSettings = rawSettings.Replace("CloudlogHelper.LogService.ClublogThirdPartyLogService, CloudlogHelper", "ClublogThirdPartyLogService");
            rawSettings = rawSettings.Replace("CloudlogHelper.LogService.EqslThirdPartyLogService, CloudlogHelper", "EqslThirdPartyLogService");
            rawSettings = rawSettings.Replace("CloudlogHelper.LogService.HamCQThirdPartyLogService, CloudlogHelper", "HamCQThirdPartyLogService");
            rawSettings = rawSettings.Replace("CloudlogHelper.LogService.HRDLogThirdPartyLogService, CloudlogHelper", "HRDLogThirdPartyLogService");
            rawSettings = rawSettings.Replace("CloudlogHelper.LogService.LoTWThirdPartyLogService, CloudlogHelper", "LoTWThirdPartyLogService");
            rawSettings = rawSettings.Replace("CloudlogHelper.LogService.QRZThirdPartyLogService, CloudlogHelper", "QRZThirdPartyLogService");

            var rootNode = JsonNode.Parse(rawSettings);
            if (rootNode is null) return rawSettings;

            var cloudlogSettingsNode = rootNode["CloudlogSettings"];
            if (cloudlogSettingsNode is JsonObject cloudlogSettingsObj &&
                cloudlogSettingsObj.TryGetPropertyValue("CloudlogStationInfo", out var stationInfoNode))
            {
                if (stationInfoNode is JsonObject stationInfoObj &&
                    stationInfoObj.TryGetPropertyValue("station_id", out var stationIdNode))
                {
                    var stationId = stationIdNode?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(stationId))
                        cloudlogSettingsObj["CloudlogStationInfoId"] = JsonValue.Create(stationId);
                }

                cloudlogSettingsObj.Remove("CloudlogStationInfo");
            }

            return rootNode.ToJsonString();
        }
        catch
        {
            return rawSettings;
        }
    }
}
