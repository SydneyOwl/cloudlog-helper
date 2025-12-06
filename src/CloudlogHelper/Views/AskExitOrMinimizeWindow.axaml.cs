using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia.ReactiveUI;
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