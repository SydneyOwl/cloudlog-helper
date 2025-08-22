using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using CloudlogHelper.Views;
using NLog;

namespace CloudlogHelper.Services;

public class WindowNotificationManagerService : IWindowNotificationManagerService, IDisposable
{
    private readonly Logger _classLoggger = LogManager.GetCurrentClassLogger();
    private WindowNotificationManager? _manager;

    public WindowNotificationManagerService(IClassicDesktopStyleApplicationLifetime topLevel)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                while (topLevel.MainWindow is not MainWindow) await Task.Delay(100);
                _manager = new WindowNotificationManager(topLevel.MainWindow);
            }
            catch (Exception e)
            {
                _classLoggger.Error(e);
            }
        });
    }

    public WindowNotificationManagerService(Window topLevel)
    {
        _manager = new WindowNotificationManager(topLevel);
    }

    public void Dispose()
    {
        // TODO release managed resources here
    }

    public async Task SendInfoNotificationAsync(string message)
    {
        await SendNotificationAsync(TranslationHelper.GetString(LangKeys.titleinfo), message,
            NotificationType.Information);
    }

    public async Task SendSuccessNotificationAsync(string message)
    {
        await SendNotificationAsync(TranslationHelper.GetString(LangKeys.titlesuccess), message,
            NotificationType.Success);
    }

    public async Task SendWarningNotificationAsync(string message)
    {
        await SendNotificationAsync(TranslationHelper.GetString(LangKeys.titlewarning), message,
            NotificationType.Warning);
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

    private async Task SendNotificationAsync(string title, string message, NotificationType tp)
    {
        if (string.IsNullOrEmpty(message)) return;
        await Dispatcher.UIThread.InvokeAsync(() => { _manager?.Show(new Notification(title, message, tp)); });
    }

    private void SendNotificationSync(string title, string message, NotificationType tp)
    {
        if (string.IsNullOrEmpty(message)) return;
        Dispatcher.UIThread.Invoke(() => { _manager?.Show(new Notification(title, message, tp)); });
    }
}