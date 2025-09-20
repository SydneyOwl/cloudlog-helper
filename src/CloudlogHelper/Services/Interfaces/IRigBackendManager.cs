using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;

namespace CloudlogHelper.Services.Interfaces;

public interface IRigBackendManager
{
    Task InitializeAsync();

    RigBackendServiceEnum GetServiceType();
    IRigService GetServiceByName(RigBackendServiceEnum rigBackend);

    bool IsServiceRunning();
    
    Task RestartService();
    Task StopService();
    Task StartService();

    Task<List<RigInfo>> GetSupportedRigModels();

    Task<RadioData> GetAllRigInfo();

    Task<string> GetServiceVersion();

    bool GetPollingAllowed();
    int GetPollingInterval();

    Task ExecuteTest(RigBackendServiceEnum backendServiceEnum,
        ApplicationSettings draftSettings);
}