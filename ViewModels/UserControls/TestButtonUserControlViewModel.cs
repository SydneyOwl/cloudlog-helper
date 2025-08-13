using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reflection;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels.UserControls;

public class TestButtonUserControlViewModel : ViewModelBase
{
    //shared with SetTestButtonCommand
    private readonly CompositeDisposable _sharedDisposables = new();

    private ObservableAsPropertyHelper<bool> _checkExecuting;

    public TestButtonUserControlViewModel()
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
    [Reactive] public ReactiveCommand<Unit, Unit>? TestCommand { get; set; }
    public bool CheckExecuting => _checkExecuting.Value;
    
    public void SetTestButtonCommand(ReactiveCommand<Unit, Unit> cmd)
    {
        TestCommand = cmd;
        TestCommand?
            .Subscribe(_ => 
            {
                CheckPassed = true;
                Observable.Timer(TimeSpan.FromSeconds(5))
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Subscribe(_ => CheckPassed = false)
                    .DisposeWith(_sharedDisposables);
            })
            .DisposeWith(_sharedDisposables);
    
        
        TestCommand?
            .ThrownExceptions
            .Subscribe(async void (ex) => 
            {
                CheckPassed = false;
            })
            .DisposeWith(_sharedDisposables);
    }
    
}