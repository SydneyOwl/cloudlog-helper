using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CloudlogHelper.Models;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.ViewModels;
using CloudlogHelper.ViewModels.UserControls;
using CloudlogHelper.Views;
using DesktopNotifications;
using DesktopNotifications.FreeDesktop;
using DesktopNotifications.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace CloudlogHelper.Services;

public static class ServiceCollectionExtensions
{
    public static Task AddCommonServicesAsync(this IServiceCollection services)
    {
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<IRigctldService, RigctldService>();
        services.AddSingleton<IUdpServerService, UdpServerService>();
        return Task.CompletedTask;
    }

    public static Task AddViewModelsAsync(this IServiceCollection services)
    {
        // Register all view models as singletons
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<SettingsWindowViewModel>();
        services.AddTransient<AskExitOrMinimizeWindowViewModel>();
        services.AddTransient<QsoSyncAssistantWindowViewModel>();
        services.AddSingleton<AboutWindowViewModel>();
        services.AddSingleton<UDPLogInfoGroupboxUserControlViewModel>();
        services.AddSingleton<RIGDataGroupboxUserControlViewModel>();
        services.AddSingleton<UserBasicDataGroupboxUserControlViewModel>();
        services.AddSingleton<StatusLightUserControlViewModel>();
        return Task.CompletedTask;
    }

    public static async Task AddExtraAsync(this IServiceCollection services)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var context = WindowsApplicationContext.FromCurrentProcess();
                var windowsNotificationManager = new WindowsNotificationManager(context);
                await windowsNotificationManager.Initialize();
                services.AddSingleton<INotificationManager>(windowsNotificationManager);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var context = FreeDesktopApplicationContext.FromCurrentProcess();
                var freeDesktopNotificationManager = new FreeDesktopNotificationManager(context);
                await freeDesktopNotificationManager.Initialize();
                services.AddSingleton<INotificationManager>(freeDesktopNotificationManager);
            }
            else
            {
                services.AddSingleton<INotificationManager>(new DefaultDesktopNotificationManager());
            }
        }
        catch (Exception e)
        {
            services.AddSingleton<INotificationManager>(new DefaultDesktopNotificationManager());
        }
        
        services.AddAutoMapper(cfg =>
        {
            cfg.CreateMap<HamlibSettings, HamlibSettings>();
            cfg.CreateMap<CloudlogSettings, CloudlogSettings>();
            cfg.CreateMap<UDPServerSettings, UDPServerSettings>();
            cfg.CreateMap<QsoSyncAssistantSettings, QsoSyncAssistantSettings>();
            cfg.CreateMap<ApplicationSettings, ApplicationSettings>();
        });
        // Register all view models as singletons
        services.AddSingleton<MainWindow>();
    }
}