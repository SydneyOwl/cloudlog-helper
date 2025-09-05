using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
using CloudlogHelper.Enums;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
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

    private readonly IInAppNotificationService _inAppNotification;
    private readonly IApplicationSettingsService _applicationSettingsService;

    private bool _isManualClosing;

    public MainWindow()
    {
        DataContext = new MainWindowViewModel();
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel mainWindowViewModel,
        IApplicationSettingsService ss,
        IWindowManagerService windowManagerService,
        IInAppNotificationService wm)
    {
        _inAppNotification = wm;
        
        _applicationSettingsService = ss;
        DataContext = mainWindowViewModel;
        InitializeComponent();
        this.WhenActivated(disposables =>
        {
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
                        var mode = _applicationSettingsService.GetCurrentSettings().ShutdownMode;
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
                        
                        if (await windowManagerService.CreateAndShowWindowByVm<bool>(typeof(AskExitOrMinimizeWindowViewModel)))
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

            // Start qso assistant, if required.
            if (_applicationSettingsService.GetCurrentSettings().QsoSyncAssistantSettings.ExecuteOnStart)
            {
                var qsoSyncAssistantWindowViewModel = windowManagerService.GetViewModelInstance<QsoSyncAssistantWindowViewModel>();
                qsoSyncAssistantWindowViewModel.EnableExecuteOnStart();
                windowManagerService.Track(new QsoSyncAssistantWindow{DataContext = qsoSyncAssistantWindowViewModel});
                
                Observable.Timer(TimeSpan.FromMilliseconds(2000))
                    .Select(_ => Unit.Default)
                    .Do(_ => _inAppNotification.SendInfoNotificationSync(
                        TranslationHelper.GetString(LangKeys.qsosyncing)))
                    .InvokeCommand(qsoSyncAssistantWindowViewModel.StartSyncCommand)
                    .DisposeWith(disposables);
            }
        });
    }

    // close without asking
    public void CloseDirectly()
    {
        _isManualClosing = true;
        Close();
    }
}