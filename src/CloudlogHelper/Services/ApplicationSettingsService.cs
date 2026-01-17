using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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
using ReactiveUI.Validation.Contexts;
using ReactiveUI.Validation.Helpers;

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

    private readonly object _draftLock = new();
    
    private object? _currentLockThreadOwner;

    private ApplicationSettings? _currentSettings;

    private ApplicationSettings? _draftSettings;
    
    private bool _isDraftLocked;

    private IMapper _mapper;

    private ApplicationSettings? _oldSettings;

    public void InjectMockSettings(ApplicationSettings settings)
    {
        _currentSettings = settings;
        _draftSettings = settings;
    }

    public void ApplySettings(object owner, List<LogSystemConfig>? rawConfigs = null)
    {
        lock (_draftLock)
        {
            if (!_isDraftLocked) throw new SynchronizationLockException("Draft setting is not locked!");

            if (!ReferenceEquals(_currentLockThreadOwner, owner))
                throw new SynchronizationLockException("Draft setting is locked by another instance!");

            ClassLogger.Trace($"Settings applied by {owner.GetType().FullName}");

            _oldSettings = _currentSettings.FastDeepClone();
            _mapper.Map(_draftSettings, _currentSettings);
            _applyLogServiceChanges(rawConfigs);

            // apply changes for log services here
            _writeCurrentSettingsToFile(_draftSettings!);
            
            _isDraftLocked = false;

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
            
            if (IsCLHServerConfChanged())
            {
                ClassLogger.Trace("clh server settings changed");
                MessageBus.Current.SendMessage(new SettingsChanged
                    { Part = ChangedPart.CLHServer });
            }
        }
    }

    public void RestoreSettings(object owner)
    {
        lock (_draftLock)
        {
            if (!_isDraftLocked) throw new SynchronizationLockException("Draft setting is not locked!");

            if (!ReferenceEquals(_currentLockThreadOwner, owner))
                throw new SynchronizationLockException("Draft setting is locked by another instance!");
            
            // make sure rig settings is not dirty
            if (IsHamlibConfChanged() || IsOmniRigConfChanged() || IsFlrigConfChanged())
            {
                ClassLogger.Trace("RIG service settings changed");
                MessageBus.Current.SendMessage(new SettingsChanged
                    { Part = ChangedPart.RigService });
            }

            _isDraftLocked = false;
            _draftSettings = _currentSettings!.FastDeepClone();
        }
    }

    public ApplicationSettings GetCurrentSettings()
    {
        return _currentSettings!;
    }

    public ApplicationSettings GetCurrentDraftSettingsSnapshot()
    {
        return _draftSettings.FastDeepClone()!;
    }

    public bool TryGetDraftSettings(object owner, out ApplicationSettings? draftSettings)
    {
        draftSettings = null;
        lock (_draftLock)
        {
            if (_isDraftLocked) return false;
            _isDraftLocked = true;
            _currentLockThreadOwner = owner;
        }

        _draftSettings!.CloudlogSettings.ReinitRules();
        _draftSettings!.HamlibSettings.ReinitRules();
        _draftSettings!.FLRigSettings.ReinitRules();
        _draftSettings!.OmniRigSettings.ReinitRules();
        _draftSettings!.UDPSettings.ReinitRules();
        _draftSettings!.CLHServerSettings.ReinitRules();
        _draftSettings!.QsoSyncAssistantSettings.ReinitRules();
        draftSettings = _draftSettings;
        return true;
    }


    private void InitEmptySettings(ThirdPartyLogService[] logServices)
    {
        _draftSettings = new ApplicationSettings();
        _draftSettings.InstanceName = CLHServerUtil.GenerateRandomInstanceName(10);
        _draftSettings.BasicSettings.LanguageType = TranslationHelper.DetectDefaultLanguage();
        _draftSettings.LogServices.AddRange(logServices);
        _currentSettings = _draftSettings.FastDeepClone();
        _oldSettings = _draftSettings.FastDeepClone();
    }

    public static ApplicationSettingsService GenerateApplicationSettingsService(ThirdPartyLogService[] logServices, bool reinit, Version version,
        IMapper mapper)
    {
        var applicationSettingsService = new ApplicationSettingsService();
        applicationSettingsService._mapper = mapper;
        if (reinit)
        {
            ClassLogger.Trace("Settings reinitializing");
            applicationSettingsService.InitEmptySettings(logServices);
            return applicationSettingsService;
        }

        try
        {
            var defaultConf = File.ReadAllText(DefaultConfigs.DefaultSettingsFile);

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
                JsonSerializer.Deserialize(defaultConf, SourceGenerationContext.Default.ApplicationSettings);
            if (applicationSettingsService._draftSettings is null)
            {
                ClassLogger.Info("Settings file not found. creating a new one instead.");
                applicationSettingsService.InitEmptySettings(logServices);
                return applicationSettingsService;
            }

            // init culture
            if (applicationSettingsService._draftSettings.BasicSettings.LanguageType == SupportedLanguage.NotSpecified)
                applicationSettingsService._draftSettings.BasicSettings.LanguageType =
                    TranslationHelper.DetectDefaultLanguage();

            // instance
            if (string.IsNullOrEmpty(applicationSettingsService._draftSettings.InstanceName))
            {
                applicationSettingsService._draftSettings.InstanceName = CLHServerUtil.GenerateRandomInstanceName(10);
            }

            var tps = applicationSettingsService._draftSettings
                .LogServices.Select(x => x.GetType()).ToArray();
            foreach (var service in logServices)
            {
                if (tps.Contains(service.GetType())) continue;
                ClassLogger.Debug(
                    $"Log service not found in settings file: {service.GetType()}. Adding to instance...");
                applicationSettingsService._draftSettings.LogServices.Add(service);
            }

            applicationSettingsService._currentSettings = applicationSettingsService._draftSettings.FastDeepClone();
            applicationSettingsService._oldSettings = applicationSettingsService._draftSettings.FastDeepClone();

            ClassLogger.Trace("Config restored successfully.");
        }
        catch (Exception e1)
        {
            ClassLogger.Warn(e1, "Failed to read settings; use default settings instead.");
            applicationSettingsService.InitEmptySettings(logServices);
        }
        finally
        {
            _writeCurrentSettingsToFile(applicationSettingsService._currentSettings!);
        }

        return applicationSettingsService;
    }

    /// <summary>
    ///     Check if cloudlog configs has been changed.
    /// </summary>
    /// <returns></returns>
    public bool IsCloudlogConfChanged()
    {
        if (_oldSettings is null) return false;
        var oldI = _oldSettings.CloudlogSettings;
        var newI = _currentSettings!.CloudlogSettings;
        return !oldI.Equals(newI);
    }

    /// <summary>
    ///     Check if hamlib configs has been changed.
    /// </summary>
    /// <returns></returns>
    public bool IsHamlibConfChanged()
    {
        if (_oldSettings is null) return false;
        return _draftSettings!.HamlibSettings.IsConfOnceChanged();
    }
    
    /// <summary>
    ///     Check if omnirig configs has been changed.
    /// </summary>
    /// <returns></returns>
    public bool IsOmniRigConfChanged()
    {
        if (_oldSettings is null) return false;
        return _draftSettings!.OmniRigSettings.IsConfOnceChanged();
    }

    /// <summary>
    ///     Check if firig configs has been changed.
    /// </summary>
    /// <returns></returns>
    public bool IsFlrigConfChanged()
    {
        if (_oldSettings is null) return false;
        var oldI = _oldSettings.FLRigSettings;
        var newI = _currentSettings!.FLRigSettings;
        return !oldI.Equals(newI);
    }

    /// <summary>
    ///     Check if udp server configs has been changed.
    /// </summary>
    /// <returns></returns>
    public bool IsUDPConfChanged()
    {
        if (_oldSettings is null) return false;
        var oldI = _oldSettings.UDPSettings;
        var newI = _currentSettings!.UDPSettings;
        return !oldI.Equals(newI);
    }
    
    /// <summary>
    ///     Check if clh server configs has been changed.
    /// </summary>
    /// <returns></returns>
    public bool IsCLHServerConfChanged()
    {
        if (_oldSettings is null) return false;
        var oldI = _oldSettings.CLHServerSettings;
        var newI = _currentSettings!.CLHServerSettings;
        return !oldI.Equals(newI);
    }

    /// <summary>
    ///     write settings to default position.
    /// </summary>
    private static void _writeCurrentSettingsToFile(ApplicationSettings settings)
    {
        try
        {
            File.WriteAllText(DefaultConfigs.DefaultSettingsFile,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    TypeInfoResolver = SourceGenerationContext.Default
                        .WithAddedModifier(JsonExtensions.IgnorePropertiesDeclaredBy<ReactiveValidationObject>())
                        .WithAddedModifier(JsonExtensions.IgnorePropertiesDeclaredBy<ReactiveObject>())
                }));
            ClassLogger.Trace(
                $"_writeCurrentSettingsToFile successfully: {DefaultConfigs.DefaultSettingsFile}");
        }
        catch (Exception e1)
        {
            ClassLogger.Error(e1, "Failed to write settings. Ignored.");
        }
    }
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(ThirdPartyLogService))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(LogSystemConfig))]
    private void _applyLogServiceChanges(List<LogSystemConfig>? rawConfigs = null)
    {
        if (rawConfigs is null) return;
        List<ApplicationSettings> settings = new() { _draftSettings!, _currentSettings! };
        foreach (var appSet in settings)
        foreach (var logService in appSet!.LogServices)
        {
            if (logService is null)
            {
                throw new NullReferenceException("Seems like some log service is null!");
            }
            var servType = logService.GetType();
            var logSystemConfig = rawConfigs.FirstOrDefault(x => x.RawType == servType);
            if (logSystemConfig is null)
            {
                ClassLogger.Warn($"Class not found for {servType.FullName}. Skipped.");
                continue;
            }

            servType.GetProperty("AutoQSOUploadEnabled")?.SetValue(logService, logSystemConfig.UploadEnabled);

            foreach (var logSystemField in logSystemConfig.Fields)
            {
                var fieldInfo = servType.GetProperty(logSystemField.PropertyName,
                    BindingFlags.Public | BindingFlags.Instance);
                if (fieldInfo is null)
                {
                    ClassLogger.Warn(
                        $"Field not found for {servType.FullName} - {logSystemField.PropertyName}. Skipped.");
                    continue;
                }

                if (fieldInfo.PropertyType == typeof(bool) && logSystemField.Value is string logVal)
                {
                    fieldInfo.SetValue(logService, logVal == "True");
                    continue;
                }

                fieldInfo.SetValue(logService, logSystemField.Value);
            }
        }
    }
}