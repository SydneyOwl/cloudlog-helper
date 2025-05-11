using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels.UserControls;

public class TestButtonViewModel : ViewModelBase
{
    //shared with SetTestButtonCommand
    private readonly CompositeDisposable _sharedDisposables = new();

    private ObservableAsPropertyHelper<bool> _checkExecuting;

    public TestButtonViewModel()
    {
        this.WhenActivated(disposables =>
        {
            _checkExecuting = this.WhenAnyValue(x => x.TestCommand)
                .Select(cmd => cmd?.IsExecuting ?? Observable.Return(false)) // just observe IsExecuting flow.....
                .Switch() // maybe command does not exist at initial?
                .ToProperty(this, x => x.CheckExecuting)
                .DisposeWith(disposables);
            disposables.Add(_sharedDisposables);
        });
    }

    [Reactive] public bool CheckPassed { get; set; }
    [Reactive] public ReactiveCommand<Unit, bool>? TestCommand { get; set; }
    public bool CheckExecuting => _checkExecuting.Value;

    public void SetTestButtonCommand(ReactiveCommand<Unit, bool> cmd)
    {
        TestCommand = cmd;
        TestCommand?
            .Where(result => result)
            .Do(_ => CheckPassed = true)
            .Delay(TimeSpan.FromSeconds(5))
            .Subscribe(_ => CheckPassed = false)
            .DisposeWith(_sharedDisposables);
    }
}