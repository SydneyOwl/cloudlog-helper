using CloudlogHelper.ViewModels;
using CloudlogHelper.ViewModels.UserControls;
using CloudlogHelper.Views;
using Microsoft.Extensions.DependencyInjection;

namespace CloudlogHelper.Services;

public static class ServiceCollectionExtensions {
    public static void AddCommonServices(this IServiceCollection collection) {
        collection.AddSingleton<IDatabaseService, DatabaseService>();
    }
    
    public static void AddViewModels(this IServiceCollection services)
    {
        // Register all view models as singletons
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<SettingsWindowViewModel>();
        services.AddSingleton<QsoSyncAssistantViewModel>();
        // services.AddSingleton<ErrorReportWindowViewModel>();
        services.AddTransient<AskExitOrMinimizeWindowViewModel>();
        services.AddSingleton<AboutWindowViewModel>();
        services.AddSingleton<UDPLogInfoGroupboxUserControlViewModel>();
        services.AddSingleton<RIGDataGroupboxUserControlViewModel>();
    }
    public static void AddExtra(this IServiceCollection services)
    {
        // Register all view models as singletons
        services.AddSingleton<MainWindow>();
    }
}