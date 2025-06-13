using System.Reactive;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using CloudlogHelper.ViewModels.UserControls;
using ReactiveUI;
using Notification = Avalonia.Controls.Notifications.Notification;

namespace CloudlogHelper.UserControls;

public partial class UserBasicDataGroupboxUserControl : ReactiveUserControl<UserBasicDataGroupboxViewModel>
{
    
    private WindowNotificationManager? _manager;
    public UserBasicDataGroupboxUserControl()
    {
        InitializeComponent();
        this.WhenActivated(disposables =>
        {
            ViewModel!.ShowNotification.RegisterHandler(DoShowNotificationAsync).DisposeWith(disposables);
        });
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var topLevel = TopLevel.GetTopLevel(this);
        _manager = new WindowNotificationManager(topLevel){ MaxItems = 3};
    }
    
    private async Task DoShowNotificationAsync(IInteractionContext<(string, string, NotificationType), Unit> interaction)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _manager?.Show(new Notification(interaction.Input.Item1, interaction.Input.Item2, interaction.Input.Item3));
            interaction.SetOutput(Unit.Default);
        });
    }
}