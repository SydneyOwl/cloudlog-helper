using System;
using System.IO;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using Newtonsoft.Json;
using NLog;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Helpers;

namespace CloudlogHelper.Models;

public enum ProgramShutdownMode
{
    /// <summary>
    ///     Shutdown mode is not specified. This means user didn't click "remember choice".
    /// </summary>
    NotSpecified,

    /// <summary>
    ///     Minimize to tray.
    /// </summary>
    ToTray,

    /// <summary>
    ///     Close the application.
    /// </summary>
    Shutdown
}

/// <summary>
///     Application-wide settings
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class ApplicationSettings : ReactiveValidationObject
{
    /// <summary>
    ///     Instance in using.
    /// </summary>
    private static ApplicationSettings? _currentInstance;


    /// <summary>
    ///     Instance for settings window.
    /// </summary>
    private static ApplicationSettings? _draftInstance;

    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();


    private ApplicationSettings()
    {
    }

    /// <summary>
    ///     ProgramShutdownMode of this application.
    /// </summary>
    [Reactive]
    [JsonProperty]
    public ProgramShutdownMode ShutdownMode { get; set; } = ProgramShutdownMode.NotSpecified;

    /// <summary>
    ///     Default language of this application.
    /// </summary>
    [Reactive]
    [JsonProperty]
    public SupportedLanguage LanguageType { get; set; } = SupportedLanguage.NotSpecified;

    /// <summary>
    ///     Cloudlog settings.
    /// </summary>
    [JsonProperty]
    public CloudlogSettings CloudlogSettings { get; set; } = new();

    /// <summary>
    ///     Third party settings.
    /// </summary>
    [JsonProperty]
    public ThirdPartyLogServiceSettings ThirdPartyLogServiceSettings { get; set; } = new();

    /// <summary>
    ///     Hamlib settings.
    /// </summary>
    [JsonProperty]
    public HamlibSettings HamlibSettings { get; set; } = new();

    /// <summary>
    ///     UDP Settings.
    /// </summary>
    [JsonProperty]
    public UDPServerSettings UDPSettings { get; set; } = new();

    /// <summary>
    ///     QSA Settings
    /// </summary>
    [JsonProperty]
    public QsoSyncAssistantSettings QsoSyncAssistantSettings { get; set; } = new();


    /// <summary>
    ///     Check if cloudlog configs has been changed.
    /// </summary>
    /// <returns></returns>
    public bool IsCloudlogConfChanged(ApplicationSettings? compare)
    {
        if (compare is null) return false;
        var oldI = compare.CloudlogSettings;
        var newI = CloudlogSettings;
        return !oldI.Equals(newI);
    }

    /// <summary>
    ///     Check if third party configs has been changed.
    /// </summary>
    /// <returns></returns>
    public bool IsThirdPartyConfChanged(ApplicationSettings? compare)
    {
        if (compare is null) return false;
        var oldI = compare.ThirdPartyLogServiceSettings;
        var newI = ThirdPartyLogServiceSettings;
        return !oldI.Equals(newI);
    }

    /// <summary>
    ///     Check if hamlib configs has been changed.
    /// </summary>
    /// <returns></returns>
    public bool IsHamlibConfChanged(ApplicationSettings? compare)
    {
        if (compare is null) return false;
        var oldI = compare.HamlibSettings;
        var newI = HamlibSettings;
        return !oldI.Equals(newI);
    }

    /// <summary>
    ///     Check if udp server configs has been changed.
    /// </summary>
    /// <returns></returns>
    public bool IsUDPConfChanged(ApplicationSettings? compare)
    {
        if (compare is null) return false;
        var oldI = compare.UDPSettings;
        var newI = UDPSettings;
        return !oldI.Equals(newI);
    }


    /// <summary>
    ///     Get an settings instance. Note this is not thread-safe.
    /// </summary>
    /// <returns></returns>
    public static ApplicationSettings GetInstance()
    {
        return _currentInstance ??= new ApplicationSettings();
    }

    public static ApplicationSettings GetDraftInstance()
    {
        return _draftInstance ??= new ApplicationSettings();
    }

    /// <summary>
    ///     Read settings from default position, then parse it as application-wide setting instance.
    /// </summary>
    public static void ReadSettingsFromFile()
    {
        try
        {
            var defaultConf = File.ReadAllText(DefaultConfigs.DefaultSettingsFile);
            _currentInstance = JsonConvert.DeserializeObject<ApplicationSettings>(defaultConf);
            _draftInstance = JsonConvert.DeserializeObject<ApplicationSettings>(defaultConf);
            ClassLogger.Trace("Calling ->DeserializeObjectSettings successfully.");
            ClassLogger.Trace($"_currentInstance null: {_currentInstance is null}");
        }
        catch (Exception e1)
        {
            ClassLogger.Warn(e1, "Failed to read settings; use default settings instead.");
            // simply ignore it
        }
    }


    /// <summary>
    ///     write settings to default position.
    /// </summary>
    public void WriteCurrentSettingsToFile()
    {
        try
        {
            File.WriteAllText(DefaultConfigs.DefaultSettingsFile, JsonConvert.SerializeObject(this, Formatting.Indented));
            ClassLogger.Trace(
                $"Calling ->WriteCurrentSettingsToFile successfully: {DefaultConfigs.DefaultSettingsFile}");
        }
        catch (Exception e1)
        {
            ClassLogger.Error(e1, "Failed to write settings. Ignored.");
        }
    }

    public ApplicationSettings DeepClone()
    {
        return JsonConvert.DeserializeObject<ApplicationSettings>(JsonConvert.SerializeObject(this))!;
    }

    public void ApplySettings()
    {
        _currentInstance!.CloudlogSettings.ApplySettingsChange(CloudlogSettings);
        _currentInstance!.ThirdPartyLogServiceSettings.ApplySettingsChange(ThirdPartyLogServiceSettings);
        _currentInstance!.HamlibSettings.ApplySettingsChange(HamlibSettings);
        _currentInstance!.UDPSettings.ApplySettingsChange(UDPSettings);
        // _settingsInstance = _currentInstance;
    }

    public void RestoreSettings()
    {
        CloudlogSettings.ApplySettingsChange(_currentInstance!.CloudlogSettings);
        ThirdPartyLogServiceSettings.ApplySettingsChange(_currentInstance!.ThirdPartyLogServiceSettings);
        HamlibSettings.ApplySettingsChange(_currentInstance!.HamlibSettings);
        UDPSettings.ApplySettingsChange(_currentInstance!.UDPSettings);
        // _settingsInstance = _currentInstance;
    }
}