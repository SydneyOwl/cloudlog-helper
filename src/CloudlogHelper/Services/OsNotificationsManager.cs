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
        Notifications.ApplicationIdentifier = "com.sydneyowl.cloudloghelper";
        Notifications.ApplicationName = "CloudlogHelper";
        Notifications.SetGuiApplication(true);
        return Task.CompletedTask;
    }

    public async Task ShowNotification(string title, string message = "", string informativeText = "")
    {
        await Notifications.ShowNotificationAsync(title, message, informativeText);
    }
}