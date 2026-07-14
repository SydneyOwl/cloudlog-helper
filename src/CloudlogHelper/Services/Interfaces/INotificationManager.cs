using System;
using System.Threading.Tasks;

namespace CloudlogHelper.Services.Interfaces;

public interface INotificationManager
{
    Task Initialize();
    Task ShowNotificationAsync(string title, string message = "", string informativeText = "");
}