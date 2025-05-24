using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels;
using NLog;
using ReactiveUI;

namespace CloudlogHelper.Views;

public partial class SettingsWindow : ReactiveWindow<SettingsWindowViewModel>
{
    private bool _triggerByClickingButton;
    
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    public SettingsWindow()
    {
        InitializeComponent();
        
        this.WhenActivated(disposables =>
        {
            var screen = Screens.ScreenFromVisual(this);
            if (!Design.IsDesignMode)
            {
                try
                {
                    if (screen == null)
                    {
                        ClassLogger.Warn("Current window is not on any screen");
                        return;
                    }

                    var workingArea = screen.WorkingArea;
                    ViewModel!.UpdateScreenInfo(workingArea.Height);
                    ClassLogger.Debug($"Current screen work area height: {workingArea.Height}");
                }
                catch (Exception e)
                {
                    ClassLogger.Warn($"Failed to fetch workarea height;{e.Message} ignored");
                }
            }
            
            ViewModel!.DiscardConf
                .Subscribe(_ =>
                {
                    _triggerByClickingButton = true;
                    Close();
                })
                .DisposeWith(disposables);

            ViewModel!.SaveAndApplyConf
                .Subscribe(_ =>
                {
                    _triggerByClickingButton = true;
                    Close();
                })
                .DisposeWith(disposables);

            Observable.FromEventPattern<EventHandler<WindowClosingEventArgs>, WindowClosingEventArgs>(
                    h => Closing += h,
                    h => Closing -= h)
                .Subscribe(async args =>
                {
                    if (!_triggerByClickingButton)
                        // Console.WriteLine("User close");
                        ViewModel!.DiscardConf.Execute().Subscribe();
                })
                .DisposeWith(disposables);
        });
    }
}