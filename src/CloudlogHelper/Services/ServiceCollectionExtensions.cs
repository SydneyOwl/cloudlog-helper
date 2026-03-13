using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using Avalonia.Controls.ApplicationLifetimes;
using CloudlogHelper.Models;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.ViewModels;
using CloudlogHelper.ViewModels.Charts;
using CloudlogHelper.ViewModels.UserControls;
using CloudlogHelper.Views;
using DesktopNotifications;
using DesktopNotifications.FreeDesktop;
#if WINDOWS
using System.Runtime.InteropServices;
using DesktopNotifications.Windows;
#endif
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace CloudlogHelper.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<IRigBackendManager, RigBackendManager>();
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<IRigService, RigctldService>();
        services.AddSingleton<IRigService, FLRigService>();
#if WINDOWS
        services.AddSingleton<IRigService, OmniRigService>();
#endif
        services.AddSingleton<IUdpServerService, UdpServerService>();
        services.AddSingleton<IQSOUploadService, QSOUploadService>();
        services.AddSingleton<IQsoQueueStore, QsoQueueStore>();
        services.AddSingleton<IChartDataCacheService, ChartDataCacheService>();
        services.AddSingleton<IDecodedDataProcessorService, DecodedDataProcessorService>();
        services.AddSingleton<ILogSystemManager, LogSystemManager>();
        services.AddSingleton<ICountryService, CountryService>();
        services.AddSingleton<IPluginService, PluginService>();
        return services;
    }

    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        services.AddTransient<SettingsWindowViewModel>();
        services.AddTransient<AskExitOrMinimizeWindowViewModel>();
        services.AddTransient<QsoSyncAssistantWindowViewModel>();
        services.AddTransient<AboutWindowViewModel>();
        
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<UDPLogInfoGroupboxUserControlViewModel>();
        services.AddSingleton<RIGDataGroupboxUserControlViewModel>();
        services.AddSingleton<UserBasicDataGroupboxUserControlViewModel>();
        services.AddSingleton<StatusLightUserControlViewModel>();
        services.AddSingleton<PolarChartWindowViewModel>();
        services.AddSingleton<StationStatisticsChartWindowViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }

    public static IServiceCollection AddRuntimeServices(this IServiceCollection services,
        IClassicDesktopStyleApplicationLifetime desktop,
        CommandLineOptions cmdOptions)
    {
        services.AddSingleton(cmdOptions);
        services.AddSingleton<IWindowManagerService, WindowManagerService>(prov =>
            new WindowManagerService(prov, desktop));
        services.AddSingleton<IInAppNotificationService, InAppNotificationService>(_ =>
            new InAppNotificationService(desktop));
        services.AddSingleton<IMessageBoxManagerService, MessageBoxManagerService>(_ =>
            new MessageBoxManagerService(desktop));
        services.AddSingleton<IClipboardService, ClipboardService>(_ =>
            new ClipboardService(desktop));

        services.AddAutoMapper(_configureMappings);
        services.AddSingleton<IApplicationSettingsService, ApplicationSettingsService>(pr =>
            ApplicationSettingsService.GenerateApplicationSettingsService(
                pr.GetRequiredService<ILogSystemManager>(),
                cmdOptions.ReinitSettings,
                pr.GetRequiredService<IDatabaseService>().GetVersionBeforeUpdate(),
                pr.GetRequiredService<IMapper>()));
        return services;
    }

    public static async Task<IServiceCollection> AddPlatformNotificationAsync(this IServiceCollection services)
    {
        try
        {
            if (OperatingSystem.IsWindows() &&
                Environment.OSVersion.Version >= new Version(10, 0))
            {
#if WINDOWS
                // only enabled on win10 or later
                ClassLogger.Info("Using windows native notification.");
                var context = WindowsApplicationContext.FromCurrentProcess();
                var windowsNotificationManager = new WindowsNotificationManager(context);
                await windowsNotificationManager.Initialize();
                services.AddSingleton<INotificationManager>(windowsNotificationManager);
#endif
            }
            else if (OperatingSystem.IsLinux())
            {
                ClassLogger.Info("Using dbus native notification.");
                var context = FreeDesktopApplicationContext.FromCurrentProcess();
                var freeDesktopNotificationManager = new FreeDesktopNotificationManager(context);
                await freeDesktopNotificationManager.Initialize();
                services.AddSingleton<INotificationManager>(freeDesktopNotificationManager);
            }
            else if (OperatingSystem.IsMacOS())
            {
                ClassLogger.Info("Using fallback notification for macOS.");
                services.AddSingleton<INotificationManager>(new DefaultDesktopNotificationManager());
            }
            else
            {
                ClassLogger.Info("Using fallback notification.");
                services.AddSingleton<INotificationManager>(new DefaultDesktopNotificationManager());
            }
        }
        catch (Exception e)
        {
            ClassLogger.Warn(e, "Failed to apply native notification - Using fallback options.");
            services.AddSingleton<INotificationManager>(new DefaultDesktopNotificationManager());
        }
        
        return services;
    }
    
    private static void _configureMappings(IMapperConfigurationExpression cfg)
    {
        var sameTypeMappings = new List<Type>
        {
            typeof(HamlibSettings),
            typeof(FLRigSettings),
            typeof(OmniRigSettings),
            typeof(CloudlogSettings),
            typeof(UDPServerSettings),
            typeof(QsoSyncAssistantSettings),
            typeof(BasicSettings),
            typeof(ApplicationSettings)
        };

        foreach (var mapType in sameTypeMappings) cfg.CreateMap(mapType, mapType);
    }
}
