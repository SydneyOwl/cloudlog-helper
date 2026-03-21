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

public class InAppNotificationService : IInAppNotificationService, IDisposable
{
    private WindowNotificationManager? _manager;
    private IClassicDesktopStyleApplicationLifetime _desktop;

    public InAppNotificationService(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;
    }

    public InAppNotificationService(Window? topLevel)
    {
        _manager = new WindowNotificationManager(topLevel);
    }

    public void Dispose()
    {
        // TODO release managed resources here
    }

    public async Task SendInfoNotificationAsync(string message)
    {
        await SendNotificationAsync(TranslationHelper.GetString(LangKeys.NotificationTitleInfo), message,
            NotificationType.Information);
    }

    public async Task SendSuccessNotificationAsync(string message)
    {
        await SendNotificationAsync(TranslationHelper.GetString(LangKeys.NotificationTitleSuccess), message,
            NotificationType.Success);
    }

    public async Task SendWarningNotificationAsync(string message)
    {
        await SendNotificationAsync(TranslationHelper.GetString(LangKeys.NotificationTitleWarning), message,
            NotificationType.Warning);
    }

    public async Task SendErrorNotificationAsync(string message)
    {
        await SendNotificationAsync(TranslationHelper.GetString(LangKeys.NotificationTitleError), message, NotificationType.Error);
    }

    public void SendInfoNotificationSync(string message)
    {
        SendNotificationSync(TranslationHelper.GetString(LangKeys.NotificationTitleInfo), message, NotificationType.Information);
    }

    public void SendSuccessNotificationSync(string message)
    {
        SendNotificationSync(TranslationHelper.GetString(LangKeys.NotificationTitleSuccess), message, NotificationType.Success);
    }

    public void SendWarningNotificationSync(string message)
    {
        SendNotificationSync(TranslationHelper.GetString(LangKeys.NotificationTitleWarning), message, NotificationType.Warning);
    }

    public void SendErrorNotificationSync(string message)
    {
        SendNotificationSync(TranslationHelper.GetString(LangKeys.NotificationTitleError), message, NotificationType.Error);
    }

    private async Task SendNotificationAsync(string title, string message, NotificationType tp)
    {
        _initManager();
        if (string.IsNullOrEmpty(message)) return;
        await Dispatcher.UIThread.InvokeAsync(() => { _manager?.Show(new Notification(title, message, tp)); });
    }

    private void SendNotificationSync(string title, string message, NotificationType tp)
    {
        _initManager();
        if (string.IsNullOrEmpty(message)) return;
        Dispatcher.UIThread.Invoke(() => { _manager?.Show(new Notification(title, message, tp)); });
    }

    private void _initManager()
    {
        if (_manager is not null)return;
        Dispatcher.UIThread.Invoke(() =>
        {
            _manager = new WindowNotificationManager(_desktop.MainWindow);
        });
    }
}