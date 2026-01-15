using System;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels;

public class AboutWindowViewModel : ViewModelBase
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly IWindowManagerService _windowManagerService;
    
    [Reactive] public bool IsDevTagVisible { get; set; }
    [Reactive] public string DevTagName { get; set; }
    
    [Reactive] public bool IsBuildTypeTagVisible { get; set; }
    [Reactive] public string BuildTypeTagName { get; set; }

    public AboutWindowViewModel()
    {
    }

    public AboutWindowViewModel(IWindowManagerService windowManager)
    {
        _windowManagerService = windowManager;
        OpenRepo = ReactiveCommand.CreateFromTask(_openRepo);
        _updateBuildStatus();
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
            ClassLogger.Error(e, "Error while opening an uri");
        }
    }

    private void _updateBuildStatus()
    {
        try
        {
            if (VersionInfo.BuildType != "NORMAL" && VersionInfo.BuildType != "@INTERNAL_BUILDTYPE@")
            {
                IsBuildTypeTagVisible = true;
                BuildTypeTagName = VersionInfo.BuildType;
            }

            if (VersionInfo.Version == "@INTERNAL_VERSION@")
            {
                IsDevTagVisible = true;
                DevTagName = "DEV";
            }
            else if (!Version.TryParse(VersionInfo.Version, out _))
            {
                IsDevTagVisible = true;
                var tag = VersionInfo.Version.Split("-").Last();
                DevTagName = tag;
            }
        }
        catch
        {
            // ignored
        }
    }
}