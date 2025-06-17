using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Notification = Avalonia.Controls.Notifications.Notification;

namespace CloudlogHelper.Utils;

public class WindowNotification
{
    private static WindowNotificationManager Manager;

    public static void SetTopLevel(Window topLevel)
    {
        Manager = new WindowNotificationManager(topLevel) { MaxItems = 3 };
    }

    private static async Task SendNotificationAsync(string title, string message, NotificationType tp)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { Manager?.Show(new Notification(title, message, tp)); });
    }

    private static void SendNotificationSync(string title, string message, NotificationType tp)
    {
        Dispatcher.UIThread.Invoke(() => { Manager?.Show(new Notification(title, message, tp)); });
    }

    public static async Task SendInfoNotificationAsync(string message)
    {
        await SendNotificationAsync(TranslationHelper.GetString("titleinfo"), message, NotificationType.Information);
    }

    public static async Task SendSuccessNotificationAsync(string message)
    {
        await SendNotificationAsync(TranslationHelper.GetString("titlesuccess"), message, NotificationType.Success);
    }

    public static async Task SendWarningNotificationAsync(string message)
    {
        await SendNotificationAsync(TranslationHelper.GetString("titlewarning"), message, NotificationType.Warning);
    }

    public static async Task SendErrorNotificationAsync(string message)
    {
        await SendNotificationAsync(TranslationHelper.GetString("titleerror"), message, NotificationType.Error);
    }

    public static void SendInfoNotificationSync(string message)
    {
        SendNotificationSync(TranslationHelper.GetString("titleinfo"), message, NotificationType.Information);
    }

    public static void SendSuccessNotificationSync(string message)
    {
        SendNotificationSync(TranslationHelper.GetString("titlesuccess"), message, NotificationType.Success);
    }

    public static void SendWarningNotificationSync(string message)
    {
        SendNotificationSync(TranslationHelper.GetString("titlewarning"), message, NotificationType.Warning);
    }

    public static void SendErrorNotificationSync(string message)
    {
        SendNotificationSync(TranslationHelper.GetString("titleerror"), message, NotificationType.Error);
    }
}