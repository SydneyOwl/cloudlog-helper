using System.Reactive;
using CloudlogHelper.Enums;
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
        ConfirmToTray = ReactiveCommand.Create(() =>
        {
            if (RememberChoice)
                if (ss.TryGetDraftSettings(this, out var draft))
                {
                    draft!.ShutdownMode =
                        MinimizeToTray ? ProgramShutdownMode.ToTray : ProgramShutdownMode.Shutdown;
                    ss.ApplySettings(this);
                }

            return MinimizeToTray;
        });
    }

    [Reactive] public bool RememberChoice { get; set; }
    [Reactive] public bool MinimizeToTray { get; set; } = true;

    public ReactiveCommand<Unit, bool> ConfirmToTray { get; }
}