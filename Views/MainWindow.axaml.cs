using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
using CloudlogHelper.Models;
using CloudlogHelper.ViewModels;
using NLog;
using ReactiveUI;

namespace CloudlogHelper.Views;

public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private bool _isManualClosing;

    public MainWindow()
    {
        InitializeComponent();
        this.WhenActivated(disposables =>
        {
            ViewModel!.ShowNewWindow
                .RegisterHandler(DoShowDialogAsync)
                .DisposeWith(disposables);

            Observable.FromEventPattern<EventHandler<WindowClosingEventArgs>, WindowClosingEventArgs>(
                    h => Closing += h,
                    h => Closing -= h)
                .Subscribe(async void (args) =>
                {
                    try
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

                        var dialog = new AskExitOrMinimizeWindow
                            { DataContext = new AskExitOrMinimizeWindowViewModel() };
                        if (await dialog.ShowDialog<bool>(this))
                        {
                            Hide();
                            return;
                        }

                        _isManualClosing = true;
                        Close();
                    }
                    catch (Exception e)
                    {
                        ClassLogger.Error(e, "Failed to hide or close window");
                    }
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
            QsoSyncAssistantViewModel vm => new QsoSyncAssistantWindow { DataContext = vm },
            _ => throw new NotSupportedException($"ViewModel not supported: {viewModel.GetType().Name}")
        };

        await newWindow.ShowDialog(this);
        interaction.SetOutput(Unit.Default);
    }
}