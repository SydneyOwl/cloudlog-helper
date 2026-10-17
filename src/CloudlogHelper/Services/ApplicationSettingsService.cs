using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using AutoMapper;
using CloudlogHelper.Enums;
using CloudlogHelper.LogService;
using CloudlogHelper.Messages;
using CloudlogHelper.Migration;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using NLog;
using ReactiveUI;

namespace CloudlogHelper.Services;

/// <summary>
///     I'll definitely refactor it someday - but not now! 
/// </summary>
public class ApplicationSettingsService : IApplicationSettingsService
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly SemaphoreSlim _draftSemaphore = new(1, 1);
    
    private object? _draftOwner;

    private ApplicationSettings? _currentSettings;

    private ApplicationSettings? _draftSettings;

    private IMapper _mapper;

    private ILogSystemManager _logSystemManager;
    
    private ApplicationSettings? _oldSettings;

    /// <summary>
    ///     Where settings are persisted. Injectable so tests cannot overwrite the real file.
    /// </summary>
    private string _settingsFilePath = DefaultConfigs.DefaultSettingsFile;

    public void InjectMockSettings(ApplicationSettings settings)
    {
        _currentSettings = settings;
        _draftSettings = settings;
    }

    public void ApplySettings(object owner, List<LogSystemConfig>? rawConfigs = null)
    {
        if (!ReferenceEquals(_draftOwner, owner))
            throw new SynchronizationLockException("Draft setting is not locked by the caller.");

        try
        {
            ClassLogger.Trace($"Settings applied by {owner.GetType().FullName}");

            _oldSettings = _currentSettings.DeepClone();
            // The in-place map below moves the draft's log service instances into the live
            // settings by reference (AutoMapper cannot map the abstract element type), so
            // unapplied draft edits would leak straight into the running services. Clone the
            // service objects instead, and keep the live list object itself - rig reporting
            // captured that list at startup and never re-reads it.
            var clonedLogServices = _draftSettings!.LogServices.DeepClone()!;
            var liveLogServices = _currentSettings!.LogServices ??= new List<ThirdPartyLogService>();
            _mapper.Map(_draftSettings, _currentSettings);
            liveLogServices.Clear();
            liveLogServices.AddRange(clonedLogServices);
            _currentSettings.LogServices = liveLogServices;
            _applyLogServiceChanges(rawConfigs);

            // apply changes for log services here
            _writeCurrentSettingsToFile(_draftSettings!);

            if (IsBasicConfChanged())
            {
                ClassLogger.Trace("Basic settings changed");
                MessageBus.Current.SendMessage(new SettingsChanged(){Part = ChangedPart.BasicSettings});
            }

            if (IsCloudlogConfChanged())
            {
                ClassLogger.Trace("Cloudlog settings changed");
                MessageBus.Current.SendMessage(new SettingsChanged { Part = ChangedPart.Cloudlog });
            }

            if (IsHamlibConfChanged() || IsOmniRigConfChanged() || IsFlrigConfChanged())
            {
                ClassLogger.Trace("RIG service settings changed");
                MessageBus.Current.SendMessage(new SettingsChanged
                    { Part = ChangedPart.RigService });
            }

            if (IsUDPConfChanged())
            {
                ClassLogger.Trace("udp settings changed");
                MessageBus.Current.SendMessage(new SettingsChanged
                    { Part = ChangedPart.UDPServer });
            }
        }
        finally
        {
            _draftOwner = null;
            try
            {
                _draftSemaphore.Release();
            }
            catch
            {
                // ignored
            }
        }
    }

    public void RestoreSettings(object owner)
    {
        if (!ReferenceEquals(_draftOwner, owner))
            throw new SynchronizationLockException("Draft setting is not locked by the caller.");

        try
        {
            // make sure rig settings is not dirty
            if (IsHamlibConfChanged() || IsOmniRigConfChanged() || IsFlrigConfChanged())
            {
                ClassLogger.Trace("RIG service settings changed");
                MessageBus.Current.SendMessage(new SettingsChanged
                    { Part = ChangedPart.RigService });
            }

            _draftSettings = _currentSettings!.DeepClone();
        }
        finally
        {
            _draftOwner = null;
            try { _draftSemaphore.Release(); } catch { /* ignore */ }
        }
    }

    public ApplicationSettings GetCurrentSettings()
    {
        return _currentSettings!;
    }

    public ApplicationSettings GetCurrentDraftSettingsSnapshot()
    {
        return _draftSettings.DeepClone()!;
    }

    public bool TryGetDraftSettings(object owner, out ApplicationSettings? draftSettings)
    {
        draftSettings = null;

        if (!_draftSemaphore.Wait(0))
        {
            return false;
        }

        // The semaphore is already held from here on, so a failure below has to hand it back:
        // otherwise _draftOwner stays set and every settings window is dead until restart.
        try
        {
            _draftOwner = owner;

            _draftSettings!.CloudlogSettings.ReinitRules();
            _draftSettings!.HamlibSettings.ReinitRules();
            _draftSettings!.FLRigSettings.ReinitRules();
            _draftSettings!.OmniRigSettings.ReinitRules();
            _draftSettings!.UDPSettings.ReinitRules();
            _draftSettings!.QsoSyncAssistantSettings.ReinitRules();
            draftSettings = _draftSettings;
            return true;
        }
        catch
        {
            _draftOwner = null;
            try
            {
                _draftSemaphore.Release();
            }
            catch
            {
                // ignored
            }

            throw;
        }
    }


    private void InitEmptySettings(ThirdPartyLogService[] logServices)
    {
        _draftSettings = new ApplicationSettings();
        _draftSettings.SkipWizard = false;
        _draftSettings.BasicSettings.InstanceName = ApplicationStartUpUtil.GenerateRandomInstanceName(10);
        _draftSettings.BasicSettings.LanguageType = TranslationHelper.DetectDefaultLanguage();
        _draftSettings.LogServices.AddRange(logServices);
        _currentSettings = _draftSettings.DeepClone();
        _oldSettings = _draftSettings.DeepClone();
    }

    public static ApplicationSettingsService GenerateApplicationSettingsService(ILogSystemManager logSystemManager,
        bool reinit, Version version,
        IMapper mapper, string? settingsFilePath = null)
    {
        var applicationSettingsService = new ApplicationSettingsService
        {
            _settingsFilePath = settingsFilePath ?? DefaultConfigs.DefaultSettingsFile
        };
        applicationSettingsService._mapper = mapper;
        applicationSettingsService._logSystemManager = logSystemManager;
        var spLogServices = logSystemManager.GetEmptySupportedLogServices();
        if (reinit)
        {
            ClassLogger.Trace("Settings reinitializing");
            applicationSettingsService.InitEmptySettings(spLogServices);
            return applicationSettingsService;
        }

        try
        {
            var defaultConf = File.ReadAllText(applicationSettingsService._settingsFilePath);

            ClassLogger.Debug($"Got current version {version}");
            if (version != Version.Parse("0.0.0"))
            {
                if (version < Version.Parse("0.3.0"))
                {
                    ClassLogger.Debug("Migrating MigrateSettings_B4_0_3_0");
                    defaultConf = SettingsMigration.MigrateSettings_B4_0_3_0(defaultConf);
                }
            }
            
            applicationSettingsService._draftSettings =
                JsonSerializer.Deserialize<ApplicationSettings>(defaultConf, AppJsonSerializerOptions.Settings);
            if (applicationSettingsService._draftSettings is null)
            {
                ClassLogger.Info("Settings file not found. creating a new one instead.");
                applicationSettingsService.InitEmptySettings(spLogServices);
                return applicationSettingsService;
            }

            // init culture
            if (applicationSettingsService._draftSettings.BasicSettings.LanguageType == SupportedLanguage.NotSpecified)
                applicationSettingsService._draftSettings.BasicSettings.LanguageType =
                    TranslationHelper.DetectDefaultLanguage();

            // instance
            if (string.IsNullOrEmpty(applicationSettingsService._draftSettings.BasicSettings.InstanceName))
            {
                applicationSettingsService._draftSettings.BasicSettings.InstanceName = ApplicationStartUpUtil.GenerateRandomInstanceName(10);
            }

            var tps = applicationSettingsService._draftSettings
                .LogServices.Select(x => x.GetType()).ToArray();
            foreach (var service in spLogServices)
            {
                if (tps.Contains(service.GetType())) continue;
                ClassLogger.Debug(
                    $"Log service not found in settings file: {service.GetType()}. Adding to instance...");
                applicationSettingsService._draftSettings.LogServices.Add(service);
            }

            applicationSettingsService._currentSettings = applicationSettingsService._draftSettings.DeepClone();
            applicationSettingsService._oldSettings = applicationSettingsService._draftSettings.DeepClone();

            ClassLogger.Trace("Config restored successfully.");
        }
        catch (Exception e1)
        {
            ClassLogger.Warn(e1, "Failed to read settings; use default settings instead.");
            applicationSettingsService.InitEmptySettings(spLogServices);
        }
        finally
        {
            _ = logSystemManager.PreInitLogSystem(applicationSettingsService._draftSettings!.LogServices);
            applicationSettingsService._writeCurrentSettingsToFile(applicationSettingsService._currentSettings!);
        }

        return applicationSettingsService;
    }
    
    /// <summary>
    ///     Check if a settings section differs between the last saved and the current settings.
    /// </summary>
    private bool IsSectionChanged(Func<ApplicationSettings, object?> section)
    {
        if (_oldSettings is null) return false;
        return !Equals(section(_oldSettings), section(_currentSettings!));
    }

    /// <summary>
    ///     Check if basic configs has been changed.
    /// </summary>
    /// <returns></returns>
    public bool IsBasicConfChanged() => IsSectionChanged(s => s.BasicSettings);

    /// <summary>
    ///     Check if cloudlog configs has been changed.
    /// </summary>
    /// <returns></returns>
    public bool IsCloudlogConfChanged() => IsSectionChanged(s => s.CloudlogSettings);

    /// <summary>
    ///     Check if hamlib configs has been changed.
    /// </summary>
    /// <returns></returns>
    // NOTE: intentionally flag-based (draft touched) rather than old-vs-current comparison, see IsConfOnceChanged.
    public bool IsHamlibConfChanged()
    {
        if (_oldSettings is null) return false;
        return _draftSettings!.HamlibSettings.IsConfOnceChanged();
    }

    /// <summary>
    ///     Check if omnirig configs has been changed.
    /// </summary>
    /// <returns></returns>
    // NOTE: intentionally flag-based (draft touched) rather than old-vs-current comparison, see IsConfOnceChanged.
    public bool IsOmniRigConfChanged()
    {
        if (_oldSettings is null) return false;
        return _draftSettings!.OmniRigSettings.IsConfOnceChanged();
    }

    /// <summary>
    ///     Check if firig configs has been changed.
    /// </summary>
    /// <returns></returns>
    public bool IsFlrigConfChanged() => IsSectionChanged(s => s.FLRigSettings);

    /// <summary>
    ///     Check if udp server configs has been changed.
    /// </summary>
    /// <returns></returns>
    public bool IsUDPConfChanged() => IsSectionChanged(s => s.UDPSettings);

    /// <summary>
    ///     write settings to default position.
    /// </summary>
    private void _writeCurrentSettingsToFile(ApplicationSettings settings)
    {
        try
        {
            File.WriteAllText(_settingsFilePath,
                JsonSerializer.Serialize(settings, AppJsonSerializerOptions.Settings));
            ClassLogger.Trace(
                $"_writeCurrentSettingsToFile successfully: {_settingsFilePath}");
        }
        catch (Exception e1)
        {
            ClassLogger.Error(e1, "Failed to write settings. Ignored.");
        }
    }
    private void _applyLogServiceChanges(List<LogSystemConfig>? rawConfigs = null)
    {
        if (rawConfigs is null) return;
        List<ApplicationSettings> settings = new() { _draftSettings!, _currentSettings! };
        foreach (var appSet in settings)
        {
            _logSystemManager.ApplyLogServiceChanges(appSet!.LogServices, rawConfigs);
        }
    }
}
