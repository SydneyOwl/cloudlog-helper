using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels;
using ReactiveUI;

namespace CloudlogHelper.Views;

public partial class SettingsWindow : ReactiveWindow<SettingsWindowViewModel>
{
    private bool _triggerByClickingButton;

    public SettingsWindow()
    {
        InitializeComponent();
        this.WhenActivated(disposables =>
        {
            ViewModel!.DiscardConf
                .Subscribe(_ =>
                {
                    _triggerByClickingButton = true;
                    Close();
                })
                .DisposeWith(disposables);
        });
        this.WhenActivated(disposables =>
        {
            ViewModel!.SaveAndApplyConf
                .Subscribe(_ =>
                {
                    _triggerByClickingButton = true;
                    Close();
                })
                .DisposeWith(disposables);
        });
        this.WhenActivated(disposables =>
        {
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