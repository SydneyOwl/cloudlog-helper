using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
using Avalonia.VisualTree;
using CloudlogHelper.Services;
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

    private bool _triggerByClickingButton;

    public SettingsWindow()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            var notification = new InAppNotificationService(this);

            ViewModel!.Notification = notification;

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