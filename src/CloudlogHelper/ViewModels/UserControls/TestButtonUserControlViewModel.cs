using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels.UserControls;

public class TestButtonUserControlViewModel : ViewModelBase
{
    private ObservableAsPropertyHelper<bool> _checkExecuting;

    public TestButtonUserControlViewModel(ReactiveCommand<Unit, Unit> cmd)
    {
        this.WhenActivated(disposables =>
        {
            TestCommand = cmd;
            TestCommand?
                .Subscribe(_ =>
                {
                    CheckPassed = true;
                    Observable.Timer(TimeSpan.FromSeconds(5))
                        .ObserveOn(RxApp.MainThreadScheduler)
                        .Subscribe(_ => CheckPassed = false)
                        .DisposeWith(disposables);
                })
                .DisposeWith(disposables);


            TestCommand?
                .ThrownExceptions
                .Subscribe(async void (ex) => { CheckPassed = false; })
                .DisposeWith(disposables);

            _checkExecuting = this.WhenAnyValue(x => x.TestCommand)
                .Select(cmd => cmd?.IsExecuting ?? Observable.Return(false)) // just observe IsExecuting flow.....
                .Switch() // maybe command does not exist at initial?
                .ToProperty(this, x => x.CheckExecuting)
                .DisposeWith(disposables);
        });
    }

    [Reactive] public bool CheckPassed { get; set; }
    [Reactive] public ReactiveCommand<Unit, Unit>? TestCommand { get; set; }
    public bool CheckExecuting => _checkExecuting.Value;
}