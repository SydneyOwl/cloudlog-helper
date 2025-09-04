using System.Threading.Tasks;

namespace CloudlogHelper.Services.Interfaces;

public interface IInAppNotificationService
{
    Task SendInfoNotificationAsync(string message);
    Task SendSuccessNotificationAsync(string message);
    Task SendWarningNotificationAsync(string message);
    Task SendErrorNotificationAsync(string message);
    void SendInfoNotificationSync(string message);
    void SendSuccessNotificationSync(string message);
    void SendWarningNotificationSync(string message);
    void SendErrorNotificationSync(string message);
}