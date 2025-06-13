using System;
using System.Reactive;
using System.Reactive.Subjects;
using Avalonia.Controls.Notifications;
using NLog;
using ReactiveUI;

namespace CloudlogHelper.ViewModels;

public class ViewModelBase : ReactiveObject, IActivatableViewModel
{
    public ViewModelActivator Activator { get; } = new();
    
    
    /// <summary>
    ///     Show notification in view.
    /// </summary>
    public Interaction<(string, string, NotificationType), Unit> ShowNotification { get; } = new();
}