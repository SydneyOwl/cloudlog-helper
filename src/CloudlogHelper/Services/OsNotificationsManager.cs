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
        Notifications.SetApplicationIdentifier("com.sydneyowl.cloudloghelper");
        Notifications.SetApplicationName("CloudlogHelper");
        Notifications.Initialize();
        return Task.CompletedTask;
    }

    public async Task ShowNotificationAsync(string title, string message = "", string informativeText = "")
    {
        await Notifications.RequestNotificationPermissionAsync();
        await Notifications.ShowNotificationAsync(title, message, informativeText);
    }
}