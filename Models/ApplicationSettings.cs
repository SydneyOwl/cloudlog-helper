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
    ///     Application-wide settings instance. Note this is not thread-safe.
    /// </summary>
    private static ApplicationSettings? _uniqueInstance;

    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    /// <summary>
    ///     Instance used to restore settings if user clicked cancel, or to check any part of the settings changed.
    /// </summary>
    private ApplicationSettings _backupInstance;

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
    ///     Clublog settings.
    /// </summary>
    [JsonProperty]
    public ClublogSettings ClublogSettings { get; set; } = new();

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
    ///     Check if cloudlog configs has been changed.
    /// </summary>
    /// <returns></returns>
    public bool IsCloudlogConfChanged()
    {
        if (_backupInstance is null) return false;
        var oldI = _backupInstance.CloudlogSettings;
        var newI = CloudlogSettings;
        return !oldI.Equals(newI);
    }
    
    /// <summary>
    ///     Check if clublog configs has been changed.
    /// </summary>
    /// <returns></returns>
    public bool IsClublogConfChanged()
    {
        if (_backupInstance is null) return false;
        var oldI = _backupInstance.ClublogSettings;
        var newI = ClublogSettings;
        return !oldI.Equals(newI);
    }

    /// <summary>
    ///     Check if hamlib configs has been changed.
    /// </summary>
    /// <returns></returns>
    public bool IsHamlibConfChanged()
    {
        if (_backupInstance is null) return false;
        var oldI = _backupInstance.HamlibSettings;
        var newI = HamlibSettings;
        return !oldI.Equals(newI);
    }

    /// <summary>
    ///     Check if udp server configs has been changed.
    /// </summary>
    /// <returns></returns>
    public bool IsUDPConfChanged()
    {
        if (_backupInstance is null) return false;
        var oldI = _backupInstance.UDPSettings;
        var newI = UDPSettings;
        return !oldI.Equals(newI);
    }


    /// <summary>
    ///     Get an settings instance. Note this is not thread-safe.
    /// </summary>
    /// <returns></returns>
    public static ApplicationSettings GetInstance()
    {
        return _uniqueInstance ??= new ApplicationSettings();
    }

    /// <summary>
    ///     Read settings from default position, then parse it as application-wide setting instance.
    /// </summary>
    public static void ReadSettingsFromFile()
    {
        try
        {
            var defaultConf = File.ReadAllText(DefaultConfigs.DefaultSettingsFile);
            _uniqueInstance = JsonConvert.DeserializeObject<ApplicationSettings>(defaultConf);
            ClassLogger.Trace("Calling ->DeserializeObjectSettings successfully.");
        }
        catch (Exception e1)
        {
            ClassLogger.Warn($"Failed to read settings: {e1.Message}; use default settings instead.");
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
            File.WriteAllText(DefaultConfigs.DefaultSettingsFile, JsonConvert.SerializeObject(this));
            ClassLogger.Trace("Calling ->WriteCurrentSettingsToFile successfully.");
        }
        catch (Exception e1)
        {
            ClassLogger.Error($"Failed to write settings: {e1.Message}");
        }
    }


    /// <summary>
    ///     Deep clone current setting instance to _backupInstance.
    /// </summary>
    public void BackupSettings()
    {
        _backupInstance = JsonConvert.DeserializeObject<ApplicationSettings>(JsonConvert.SerializeObject(this))!;
    }


    /// <summary>
    ///     Restore settings using _uniqueInstance.
    /// </summary>
    public void RestoreSettings()
    {
        _uniqueInstance = _backupInstance;
    }
}