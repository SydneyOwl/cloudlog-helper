using System.Collections.Generic;
using CloudlogHelper.LogService;
using CloudlogHelper.Models;

namespace CloudlogHelper.Services.Interfaces;

public interface IApplicationSettingsService
{
    public bool IsCloudlogConfChanged();

    public bool IsHamlibConfChanged();

    public bool IsUDPConfChanged();

    public bool RestartHamlibNeeded();

    public bool RestartUDPNeeded();

    public void ApplySettings(object owner, List<LogSystemConfig>? rawConfigs = null);

    public void RestoreSettings(object owner);
    
    public ApplicationSettings GetCurrentSettings();
    public bool TryGetDraftSettings(object owner, out ApplicationSettings? draftSettings);
}