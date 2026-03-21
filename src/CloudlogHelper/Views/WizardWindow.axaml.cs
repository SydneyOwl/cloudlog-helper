using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels;
using ReactiveUI;

namespace CloudlogHelper.Views;

public partial class WizardWindow : ReactiveWindow<WizardWindowViewModel>
{
    private bool _triggerByCommand;

    public WizardWindow()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            this.WhenAnyValue(x => x.ViewModel!.CloseRequested)
                .Where(shouldClose => shouldClose)
                .Subscribe(_ =>
                {
                    _triggerByCommand = true;
                    Close();
                })
                .DisposeWith(disposables);

            Observable.FromEventPattern<EventHandler<WindowClosingEventArgs>, WindowClosingEventArgs>(
                    h => Closing += h,
                    h => Closing -= h)
                .Subscribe(args =>
                {
                    if (_triggerByCommand) return;
                    args.EventArgs.Cancel = true;
                    ViewModel!.SkipWizardCommand.Execute().Subscribe();
                })
                .DisposeWith(disposables);
        });

        Closed += (_, _) => ViewModel?.DisposeDraftIfNeeded();
    }
}
