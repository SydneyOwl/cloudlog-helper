using System;
using System.Threading.Tasks;
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

    public static Task AddCommonServicesAsync(this IServiceCollection services)
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
        services.AddSingleton<IChartDataCacheService, ChartDataCacheService>();
        services.AddSingleton<IDecodedDataProcessorService, DecodedDataProcessorService>();
        services.AddSingleton<ICLHServerService, CLHServerService>();
        return Task.CompletedTask;
    }

    public static Task AddViewModelsAsync(this IServiceCollection services)
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
        return Task.CompletedTask;
    }

    public static async Task AddExtraAsync(this IServiceCollection services)
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

        services.AddAutoMapper(cfg =>
        {
            cfg.CreateMap<HamlibSettings, HamlibSettings>();
            cfg.CreateMap<FLRigSettings, FLRigSettings>();
            cfg.CreateMap<OmniRigSettings, OmniRigSettings>();
            cfg.CreateMap<CloudlogSettings, CloudlogSettings>();
            cfg.CreateMap<UDPServerSettings, UDPServerSettings>();
            cfg.CreateMap<QsoSyncAssistantSettings, QsoSyncAssistantSettings>();
            cfg.CreateMap<CLHServerSettings, CLHServerSettings>();
            cfg.CreateMap<BasicSettings, BasicSettings>();
            cfg.CreateMap<ApplicationSettings, ApplicationSettings>();
        });
        // Register all view models as singletons
        services.AddSingleton<MainWindow>();
    }
}