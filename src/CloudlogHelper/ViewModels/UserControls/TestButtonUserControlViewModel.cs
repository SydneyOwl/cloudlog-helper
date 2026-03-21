using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels.UserControls;

public class TestButtonUserControlViewModel : ViewModelBase
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private readonly Func<Exception, Task>? _errorHandler;
    private ObservableAsPropertyHelper<bool> _checkExecuting;

    public TestButtonUserControlViewModel() : this(null)
    {
    }

    public TestButtonUserControlViewModel(ReactiveCommand<Unit, Unit>? cmd, Func<Exception, Task>? errorHandler = null)
    {
        _errorHandler = errorHandler;

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
                .Do(err => ClassLogger.Error(err, "Error when testing:"))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SelectMany(ex => Observable.FromAsync(async () =>
                {
                    CheckPassed = false;
                    if (_errorHandler is not null)
                    {
                        try
                        {
                            await _errorHandler(ex);
                        }
                        catch (Exception callbackEx)
                        {
                            ClassLogger.Error(callbackEx, "Error when handling test failure:");
                        }
                    }

                    return Unit.Default;
                }))
                .Subscribe()
                .DisposeWith(disposables);

            _checkExecuting = this.WhenAnyValue(x => x.TestCommand)
                .Select(c => c?.IsExecuting ?? Observable.Return(false)) // just observe IsExecuting flow.....
                .Switch() // maybe command does not exist at initial?
                .ToProperty(this, x => x.CheckExecuting)
                .DisposeWith(disposables);
        });
    }

    [Reactive] public bool CheckPassed { get; set; }
    [Reactive] public ReactiveCommand<Unit, Unit>? TestCommand { get; set; }
    public bool CheckExecuting => _checkExecuting.Value;
}
