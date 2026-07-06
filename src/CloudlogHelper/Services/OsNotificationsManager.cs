using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Data.Core;
using CloudlogHelper.Services.Interfaces;
using OsNotifications;

namespace CloudlogHelper.Services;

public class OsNotificationsManager : INotificationManager
{
    public Task Initialize()
    {
        Notifications.BundleIdentifier = "com.sydneyowl.cloudloghelper";
        Notifications.SetGuiApplication(true);
        return Task.CompletedTask;
    }

    public Task ShowNotification(string title, string message = "", string informativeText = "")
    {
        Notifications.ShowNotification(title, message, informativeText);
        return Task.CompletedTask;
    }
}