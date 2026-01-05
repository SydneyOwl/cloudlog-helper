using System;
using System.Net;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using Microsoft.Extensions.Logging;
using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Services.Interfaces;

public interface IUdpServerService
{
    bool IsUdpServerEnabled();
    
    bool IsUdpServerRunning();
    bool IsNotifyOnQsoMade();
    bool IsNotifyOnQsoUploaded();

    int QSOUploadRetryCount();

    Task InitializeAsync(Func<WsjtxMessage, Task> handler, Action<LogLevel, string> logger);
}