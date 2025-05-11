using System;
using System.Reactive.Subjects;
using NLog;
using ReactiveUI;

namespace CloudlogHelper.ViewModels;

public class ViewModelBase : ReactiveObject, IActivatableViewModel
{
    private readonly Subject<string> _messageStream = new();
    public IObservable<string> MessageStream => _messageStream;
    public ViewModelActivator Activator { get; } = new();
    
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    // send message to parent vm
    // can be an empty string which indicates no error or clear error panel..
    internal void SendMsgToParentVm(string message)
    {
        _messageStream.OnNext(message);
        // console this as well...
        ClassLogger.Debug($"Sent to vm: {message}");
    }
}