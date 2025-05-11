using System.Reactive;
using CloudlogHelper.Models;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels;

public class AskExitOrMinimizeWindowViewModel : ViewModelBase
{
    private readonly ApplicationSettings _settings;


    public AskExitOrMinimizeWindowViewModel()
    {
        _settings = ApplicationSettings.GetInstance();
        ConfirmToTray = ReactiveCommand.Create(() =>
        {
            if (RememberChoice)
            {
                _settings.ShutdownMode =
                    MinimizeToTray ? ProgramShutdownMode.ToTray : ProgramShutdownMode.Shutdown;
                _settings.WriteCurrentSettingsToFile();
            }

            return MinimizeToTray;
        });
    }

    [Reactive] public bool RememberChoice { get; set; }
    [Reactive] public bool MinimizeToTray { get; set; } = true;

    public ReactiveCommand<Unit, bool> ConfirmToTray { get; }
}