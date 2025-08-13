using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using NLog;

namespace CloudlogHelper.Services;

public class WindowNotificationManagerService:IWindowNotificationManagerService, IDisposable
{
    private WindowNotificationManager? _manager;
    private Logger _classLoggger =  LogManager.GetCurrentClassLogger();

    public WindowNotificationManagerService(IClassicDesktopStyleApplicationLifetime topLevel)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                while (topLevel.MainWindow is null)
                {
                    await Task.Delay(100);
                }
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

    public void Dispose()
    {
        // TODO release managed resources here
    }
}