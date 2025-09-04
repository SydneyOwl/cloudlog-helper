using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
using Avalonia.VisualTree;
using CloudlogHelper.Services;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.ViewModels;
using NLog;
using ReactiveUI;

namespace CloudlogHelper.Views;

public partial class SettingsWindow : ReactiveWindow<SettingsWindowViewModel>
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private IInAppNotificationService _notification;

    private bool _triggerByClickingButton;

    public SettingsWindow()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            _notification = new InAppNotificationService(this);
            // seems like linux does not support dpi reading...
            if (!Design.IsDesignMode)
                try
                {
                    var screen = Screens.ScreenFromVisual(this);
                    if (screen == null)
                    {
                        ClassLogger.Warn("Current window is not on any screen");
                        return;
                    }

                    var dpi = this.GetVisualRoot()?.RenderScaling ?? 1.0;
                    var maxHeight = screen.WorkingArea.Height / dpi;

                    Height = (int)(maxHeight * 0.7);

                    // Position = new PixelPoint(Position.X, 10);
                    ClassLogger.Debug($"Current screen work area height: {maxHeight}, dpi: {dpi}");
                }
                catch (Exception e)
                {
                    Height = 800;
                    ClassLogger.Warn(e, "Failed to fetch workarea height");
                }
            else
                Height = 800;

            ViewModel!.Notification = _notification;

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