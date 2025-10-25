using System.Collections.Generic;
using CloudlogHelper.Models;

namespace CloudlogHelper.Services.Interfaces;

public interface IApplicationSettingsService
{
    public bool RestartHamlibNeeded();
    public bool RestartFLRigNeeded();

    public bool RestartUDPNeeded();

    public void ApplySettings(object owner, List<LogSystemConfig>? rawConfigs = null);

    public void RestoreSettings(object owner);

    public ApplicationSettings GetCurrentSettings();
    public ApplicationSettings GetCurrentDraftSettingsSnapshot();
    public bool TryGetDraftSettings(object owner, out ApplicationSettings? draftSettings);
}