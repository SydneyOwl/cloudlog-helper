using System;
using System.Collections.Generic;
using CloudlogHelper.Models;
using CloudlogHelper.Services.Interfaces;
using DesktopNotifications;
using Google.Protobuf;

namespace CloudlogHelper.Services;

public class CLHServerService : IDisposable
{
    private readonly ApplicationSettings _appSettings;

    public CLHServerService(IApplicationSettingsService appSettingsService)
    {
        _appSettings = appSettingsService.GetCurrentSettings();
    }
    
    
    
    public void Dispose()
    {
        // TODO release managed resources here
    }
}