using System;
using System.Reactive;
using System.Threading.Tasks;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using NLog;
using ReactiveUI;

namespace CloudlogHelper.ViewModels;

public class AboutWindowViewModel : ViewModelBase
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly IWindowManagerService _windowManagerService;

    public AboutWindowViewModel()
    {
    }

    public AboutWindowViewModel(IWindowManagerService windowManager)
    {
        _windowManagerService = windowManager;
        OpenRepo = ReactiveCommand.CreateFromTask(_openRepo);
    }

    public ReactiveCommand<Unit, Unit> OpenRepo { get; set; }

    private async Task _openRepo()
    {
        try
        {
            await _windowManagerService.LaunchBrowser(DefaultConfigs.RepoAddress);
        }
        catch (Exception e)
        {
            ClassLogger.Error(e);
        }
    }
}