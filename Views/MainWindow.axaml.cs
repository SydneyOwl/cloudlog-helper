using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
using CloudlogHelper.Models;
using CloudlogHelper.ViewModels;
using ReactiveUI;

namespace CloudlogHelper.Views;

public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
{
    private bool _isManualClosing;

    public MainWindow()
    {
        InitializeComponent();
        this.WhenActivated(disposables =>
        {
            ViewModel!.ShowNewWindow
                .RegisterHandler(DoShowDialogAsync)
                .DisposeWith(disposables);
        });

        // stop window from closing
        this.WhenActivated(disposables =>
        {
            Observable.FromEventPattern<EventHandler<WindowClosingEventArgs>, WindowClosingEventArgs>(
                    h => Closing += h,
                    h => Closing -= h)
                .Subscribe(async args =>
                {
                    // ask users to minimize or close
                    if (_isManualClosing) return;
                    args.EventArgs.Cancel = true;
                    var mode = ApplicationSettings.GetInstance().ShutdownMode;
                    if (mode != ProgramShutdownMode.NotSpecified)
                    {
                        if (mode == ProgramShutdownMode.ToTray)
                        {
                            Hide();
                            return;
                        }

                        args.EventArgs.Cancel = false;
                        return;
                    }

                    var dialog = new AskExitOrMinimizeWindow { DataContext = new AskExitOrMinimizeWindowViewModel() };
                    if (await dialog.ShowDialog<bool>(this))
                    {
                        Hide();
                        return;
                    }

                    _isManualClosing = true;
                    Close();
                })
                .DisposeWith(disposables);
        });
    }

    // close without asking
    public void CloseDirectly()
    {
        _isManualClosing = true;
        Close();
    }

    private async Task DoShowDialogAsync(IInteractionContext<ViewModelBase, Unit> interaction)
    {
        var viewModel = interaction.Input;
        Window newWindow = viewModel switch
        {
            SettingsWindowViewModel vm => new SettingsWindow { DataContext = vm },
            AboutWindowViewModel vm => new AboutWindow { DataContext = vm },
            _ => throw new NotSupportedException($"ViewModel not supported: {viewModel.GetType().Name}")
        };

        await newWindow.ShowDialog(this);
        interaction.SetOutput(Unit.Default);
    }
}