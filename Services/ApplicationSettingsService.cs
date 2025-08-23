using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AutoMapper;
using Avalonia.Markup.Xaml.MarkupExtensions;
using CloudlogHelper.LogService;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using Force.DeepCloner;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NLog;

namespace CloudlogHelper.Services;

public class ApplicationSettingsService: IApplicationSettingsService
{
    
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    
    /// <summary>
    ///     default json serializer settings.
    /// </summary>
    private static JsonSerializerSettings _defaultSerializerSettings = new()
    {
        TypeNameHandling = TypeNameHandling.Auto,
        Formatting = Formatting.Indented
    };

    private ApplicationSettings? _currentSettings;
    
    private ApplicationSettings? _draftSettings;

    private ApplicationSettings? _oldSettings;

    private IMapper _mapper;
    
    private void InitEmptySettings(ThirdPartyLogService[] logServices)
    {
        _draftSettings = new ApplicationSettings();
        _draftSettings.LanguageType = TranslationHelper.DetectDefaultLanguage();
        _draftSettings.LogServices.AddRange(logServices);
        _currentSettings = _draftSettings.DeepClone();
        _oldSettings = _draftSettings.DeepClone();
    }

    public static ApplicationSettingsService GenerateApplicationSettingsService(ThirdPartyLogService[] logServices,
        bool reinit, IMapper mapper)
    {
        var applicationSettingsService = new ApplicationSettingsService();
        applicationSettingsService._mapper = mapper;
        if (reinit)
        {
            ClassLogger.Debug("Settings reinitializing");
            applicationSettingsService.InitEmptySettings(logServices);
            return applicationSettingsService;
        }

        try
        {
            var defaultConf = File.ReadAllText(DefaultConfigs.DefaultSettingsFile);
            applicationSettingsService._draftSettings =
                JsonConvert.DeserializeObject<ApplicationSettings>(defaultConf, _defaultSerializerSettings);
            if (applicationSettingsService._draftSettings is null)
            {
                ClassLogger.Debug("Settings file not found. creating a new one instead.");
                applicationSettingsService.InitEmptySettings(logServices);
                return applicationSettingsService;
            }

            // init culture
            if (applicationSettingsService._draftSettings.LanguageType == SupportedLanguage.NotSpecified)
            {
                applicationSettingsService._draftSettings.LanguageType = TranslationHelper.DetectDefaultLanguage(); 
            }
            
            var tps =  applicationSettingsService._draftSettings
                .LogServices.Select(x => x.GetType()).ToArray();
            foreach (var service in logServices)
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
            applicationSettingsService.InitEmptySettings(logServices);
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
        var oldI = _oldSettings.HamlibSettings;
        var newI = _currentSettings!.HamlibSettings;
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
    
    
    public bool RestartHamlibNeeded()
    {
        var a = _currentSettings!.HamlibSettings;
        var b = _oldSettings!.HamlibSettings;
        if (a.SelectedRigInfo is null) return true;
        return !a.SelectedRigInfo.Equals(b.SelectedRigInfo) || a.SelectedPort != b.SelectedPort ||
               a.PollAllowed != b.PollAllowed ||
               a.UseRigAdvanced != b.UseRigAdvanced || a.DisablePTT != b.DisablePTT ||
               a.AllowExternalControl != b.AllowExternalControl ||
               a.OverrideCommandlineArg != b.OverrideCommandlineArg ||
               a.UseExternalRigctld != b.UseExternalRigctld ||
               a.ExternalRigctldHostAddress != b.ExternalRigctldHostAddress;
    }
    
    public bool RestartUDPNeeded()
    {
        var a = _currentSettings!.UDPSettings;
        var b = _oldSettings!.UDPSettings;
        return a.EnableUDPServer != b.EnableUDPServer || a.EnableConnectionFromOutside !=
                                                              b.EnableConnectionFromOutside
                                                              || a.UDPPort != b.UDPPort;
    }
    

    /// <summary>
    ///     write settings to default position.
    /// </summary>
    private void _writeCurrentSettingsToFile(ApplicationSettings settings)
    {
        try
        {
            File.WriteAllText(DefaultConfigs.DefaultSettingsFile,
                JsonConvert.SerializeObject(settings, _defaultSerializerSettings));
            ClassLogger.Trace(
                $"_writeCurrentSettingsToFile successfully: {DefaultConfigs.DefaultSettingsFile}");
        }
        catch (Exception e1)
        {
            ClassLogger.Error(e1, "Failed to write settings. Ignored.");
        }
    }


    public void ApplySettings(List<LogSystemConfig>? rawConfigs = null)
    {
        _oldSettings = _currentSettings.DeepClone();
        // apply changes for log services here
        _writeCurrentSettingsToFile(_draftSettings!);
        _applyLogServiceChanges(rawConfigs);
        _mapper.Map(_draftSettings, _currentSettings);
        // _draftSettings.DeepCloneTo(_currentSettings);
        // _currentInstance!.CloudlogSettings.ApplySettingsChange(CloudlogSettings);
        // _currentInstance!.HamlibSettings.ApplySettingsChange(HamlibSettings);
        // _currentInstance!.UDPSettings.ApplySettingsChange(UDPSettings);
        // _settingsInstance = _currentInstance;
    }

    public void RestoreSettings()
    {
        // _currentSettings.DeepCloneTo(_draftSettings);
        _draftSettings = _currentSettings!.DeepClone();
        // CloudlogSettings.ApplySettingsChange(_currentInstance!.CloudlogSettings);
        // HamlibSettings.ApplySettingsChange(_currentInstance!.HamlibSettings);
        // UDPSettings.ApplySettingsChange(_currentInstance!.UDPSettings);
        // _settingsInstance = _currentInstance;
    }

    public ApplicationSettings GetCurrentSettings()
    {
        return _currentSettings!;
    }

    public ApplicationSettings GetDraftSettings()
    {
        return _draftSettings!;
    }

    private void _applyLogServiceChanges(List<LogSystemConfig>? rawConfigs = null)
    {
        if (rawConfigs is null) return;
        List<ApplicationSettings> settings = new() { _draftSettings!, _currentSettings! };
        foreach (var appSet in settings)
        foreach (var logService in appSet!.LogServices)
        {
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

                fieldInfo.SetValue(logService, logSystemField.Value);
            }
        }
    }
}