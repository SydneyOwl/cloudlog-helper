using System;
using System.Reactive.Disposables;
using Avalonia.ReactiveUI;
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
                .Subscribe(result => Close(result))
                .DisposeWith(disposables);
        });
    }
}