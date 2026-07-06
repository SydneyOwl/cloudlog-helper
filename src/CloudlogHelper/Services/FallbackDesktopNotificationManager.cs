using System;
using System.Threading.Tasks;
using CloudlogHelper.Services.Interfaces;

namespace CloudlogHelper.Services;

public class FallbackDesktopNotificationManager : INotificationManager
{
    public void Dispose()
    {
    }

    public Task Initialize()
    {
        return Task.CompletedTask;
    }

    public Task ShowNotification(string title, string message = "", string informativeText = "")
    {
        return Task.CompletedTask;
    }
}