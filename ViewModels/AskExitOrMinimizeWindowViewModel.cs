using System.Reactive;
using CloudlogHelper.Enums;
using CloudlogHelper.Models;
using CloudlogHelper.Services.Interfaces;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels;

public class AskExitOrMinimizeWindowViewModel : ViewModelBase
{
    public AskExitOrMinimizeWindowViewModel()
    {
        
    }
    
    public AskExitOrMinimizeWindowViewModel(IApplicationSettingsService ss)
    {
        var settings = ss.GetDraftSettings();
        ConfirmToTray = ReactiveCommand.Create(() =>
        {
            if (RememberChoice)
            {
                settings.ShutdownMode =
                    MinimizeToTray ? ProgramShutdownMode.ToTray : ProgramShutdownMode.Shutdown;
                ss.ApplySettings();
            }

            return MinimizeToTray;
        });
    }

    [Reactive] public bool RememberChoice { get; set; }
    [Reactive] public bool MinimizeToTray { get; set; } = true;

    public ReactiveCommand<Unit, bool> ConfirmToTray { get; }
}