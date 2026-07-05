using System;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Threading.Tasks;
using ReactiveUI.Avalonia;
using Avalonia.Threading;
using CloudlogHelper.ViewModels;
using ReactiveUI;

namespace CloudlogHelper.Views;

public partial class AskExitOrMinimizeWindow : ReactiveWindow<AskExitOrMinimizeWindowViewModel>
{
    public AskExitOrMinimizeWindow()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            ViewModel.ConfirmToTray
                .Subscribe( result => Dispatcher.UIThread.InvokeAsync(() => Close(result)))
                .DisposeWith(disposables);
        });
    }
}