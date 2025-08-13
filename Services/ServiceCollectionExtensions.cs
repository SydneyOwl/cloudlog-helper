using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.ViewModels;
using CloudlogHelper.ViewModels.UserControls;
using CloudlogHelper.Views;
using Microsoft.Extensions.DependencyInjection;

namespace CloudlogHelper.Services;

public static class ServiceCollectionExtensions {
    public static void AddCommonServices(this IServiceCollection services) {
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<IRigctldService, RigctldService>();
        services.AddSingleton<IUdpServerService, UdpServerService>();
    }
    
    public static void AddViewModels(this IServiceCollection services)
    {
        // Register all view models as singletons
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<SettingsWindowViewModel>();
        services.AddTransient<QsoSyncAssistantWindowViewModel>();
        services.AddTransient<AskExitOrMinimizeWindowViewModel>();
        services.AddSingleton<AboutWindowViewModel>();
        services.AddSingleton<UDPLogInfoGroupboxUserControlViewModel>();
        services.AddSingleton<RIGDataGroupboxUserControlViewModel>();
        services.AddSingleton<UserBasicDataGroupboxUserControlViewModel>();
        services.AddSingleton<StatusLightUserControlViewModel>();
    }
    public static void AddExtra(this IServiceCollection services)
    {
        // Register all view models as singletons
        services.AddSingleton<MainWindow>();
    }
}