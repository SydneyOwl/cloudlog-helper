using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CloudlogHelper.Resources;
using Notification = Avalonia.Controls.Notifications.Notification;

namespace CloudlogHelper.Utils;

public class WindowNotification
{
    private readonly WindowNotificationManager _manager;

    public WindowNotification(Window topLevel)
    {
        _manager = new WindowNotificationManager(topLevel);
    }

    private async Task SendNotificationAsync(string title, string message, NotificationType tp)
    {
        if (string.IsNullOrEmpty(message))return;
        await Dispatcher.UIThread.InvokeAsync(() => { _manager?.Show(new Notification(title, message, tp)); });
    }

    private void SendNotificationSync(string title, string message, NotificationType tp)
    {
        if (string.IsNullOrEmpty(message))return;
        Dispatcher.UIThread.Invoke(() => { _manager?.Show(new Notification(title, message, tp)); });
    }

    public async Task SendInfoNotificationAsync(string message)
    {
        await SendNotificationAsync(TranslationHelper.GetString(LangKeys.titleinfo), message, NotificationType.Information);
    }

    public async Task SendSuccessNotificationAsync(string message)
    {
        await SendNotificationAsync(TranslationHelper.GetString(LangKeys.titlesuccess), message, NotificationType.Success);
    }

    public async Task SendWarningNotificationAsync(string message)
    {
        await SendNotificationAsync(TranslationHelper.GetString(LangKeys.titlewarning), message, NotificationType.Warning);
    }

    public async Task SendErrorNotificationAsync(string message)
    {
        await SendNotificationAsync(TranslationHelper.GetString(LangKeys.titleerror), message, NotificationType.Error);
    }

    public void SendInfoNotificationSync(string message)
    {
        SendNotificationSync(TranslationHelper.GetString(LangKeys.titleinfo), message, NotificationType.Information);
    }

    public void SendSuccessNotificationSync(string message)
    {
        SendNotificationSync(TranslationHelper.GetString(LangKeys.titlesuccess), message, NotificationType.Success);
    }

    public void SendWarningNotificationSync(string message)
    {
        SendNotificationSync(TranslationHelper.GetString(LangKeys.titlewarning), message, NotificationType.Warning);
    }

    public void SendErrorNotificationSync(string message)
    {
        SendNotificationSync(TranslationHelper.GetString(LangKeys.titleerror), message, NotificationType.Error);
    }
}