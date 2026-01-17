using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.LogService;
using CloudlogHelper.LogService.Attributes;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using Flurl.Http;
using NLog;

namespace CloudlogHelper.Services;

public class LogSystemManager : ILogSystemManager, IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private static ThirdPartyLogService[]? _cachedLogServices;

    public LogSystemManager()
    {
        _cachedLogServices = _discoverLogServices();
    }

    public async Task PreInitLogSystem(IEnumerable<ThirdPartyLogService> ls)
    {
        var preinitToken = new CancellationTokenSource();
        preinitToken.CancelAfter(TimeSpan.FromSeconds(DefaultConfigs.LogServicePreinitTimeoutSec));
        
        ClassLogger.Debug("Pre-initing log services");
        // initialize services at background
        await Task.WhenAll(ls.Select(x => x.PreInitAsync(preinitToken.Token)))
            .ContinueWith(ex =>
            {
                if (ex.IsFaulted)
                {
                    ClassLogger.Error(ex.Exception, "Error while initing logservices.");
                }
                else
                {
                    ClassLogger.Debug("Pre-initing log services finished successfully.");
                }
            }, preinitToken.Token);
    }

    public ThirdPartyLogService[]? GetEmptySupportedLogServices()
    {
        return _cachedLogServices;
    }

    /// <summary>
    /// Discovers all ThirdPartyLogService implementations in the assembly.
    /// </summary>
    private ThirdPartyLogService[] _discoverLogServices()
    {
        var lType = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(LogServiceAttribute), false).Length > 0)
            .ToList();

        if (lType.GroupBy(n => n).Any(c => c.Count() > 1))
            throw new InvalidOperationException("Duplicate log service found. This is not allowed!");

        var logServices = lType.Select(x =>
        {
            if (!typeof(ThirdPartyLogService).IsAssignableFrom(x))
                throw new TypeLoadException($"Log service must be assignable to {nameof(ThirdPartyLogService)}");
            return (ThirdPartyLogService)Activator.CreateInstance(x)!;
        }).ToArray();

        return logServices;
    }

    /// <summary>
    /// Extracts field configurations from a ThirdPartyLogService instance via reflection.
    /// </summary>
    public LogSystemConfig ExtractLogSystemConfig(ThirdPartyLogService logService)
    {
        var classAttr = logService.GetType().GetCustomAttribute<LogServiceAttribute>();
        if (classAttr == null)
            throw new Exception($"Failed to find LogServiceAttribute on {logService.GetType().FullName}");

        var properties = logService.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.IsDefined(typeof(UserInputAttribute), false));

        var fields = properties.Select(prop =>
                new { prop, attr = prop.GetCustomAttribute<UserInputAttribute>()! })
            .Select(t =>
            {
                var selections = Array.Empty<string>();

                if (!string.IsNullOrWhiteSpace(t.attr.SelectionsArrayName))
                {
                    var serviceType = logService.GetType();
                    var value = serviceType
                        .GetField(t.attr.SelectionsArrayName)?
                        .GetValue(logService);

                    // try property
                    if (value is null)
                        value = serviceType
                            .GetProperty(t.attr.SelectionsArrayName)?
                            .GetValue(logService);

                    if (value is string[] ss) selections = ss;
                }

                return new LogSystemField
                {
                    DisplayNameLangKey = t.attr.DisplayNameLangKey,
                    PropertyName = t.prop.Name,
                    Type = t.attr.InputType,
                    Watermark = t.attr.WaterMark,
                    Description = t.attr.Description,
                    IsRequired = t.attr.IsRequired,
                    Selections = selections,
                    Value = t.prop.GetValue(logService)?.ToString()
                };
            }).ToList();

        return new LogSystemConfig
        {
            DisplayName = classAttr.ServiceName,
            Fields = fields,
            RawType = logService.GetType(),
            UploadEnabled = logService.AutoQSOUploadEnabled
        };
    }

    public LogSystemConfig[]? ExtractLogSystemConfigBatch(IEnumerable<ThirdPartyLogService> ls)
    {
        return ls?.Select(ExtractLogSystemConfig)?.ToArray();
    }
    
    /// <summary>
    /// Applies LogSystemConfig changes to ThirdPartyLogService instances.
    /// Used by ApplicationSettingsService to persist field values.
    /// </summary>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(ThirdPartyLogService))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(LogSystemConfig))]
    public void ApplyLogServiceChanges(List<ThirdPartyLogService> logServices, List<LogSystemConfig> rawConfigs)
    {
        if (rawConfigs is null) return;

        foreach (var logService in logServices)
        {
            if (logService is null)
            {
                throw new NullReferenceException("A log service is null!");
            }

            var servType = logService.GetType();
            var logSystemConfig = rawConfigs.FirstOrDefault(x => x.RawType == servType);
            if (logSystemConfig is null)
            {
                ClassLogger.Warn($"LogSystemConfig not found for {servType.FullName}. Skipped.");
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

    public void Dispose()
    {
        // TODO release managed resources here
    }
}