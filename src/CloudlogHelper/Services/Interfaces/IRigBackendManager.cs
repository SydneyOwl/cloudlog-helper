using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.Models;

namespace CloudlogHelper.Services.Interfaces;

public interface IRigBackendManager
{
    Task InitializeAsync();

    RigBackendServiceEnum GetServiceType();
    string GetServiceEndpointAddress();
    IRigService GetServiceByName(RigBackendServiceEnum rigBackend);

    bool IsServiceRunning();

    Task RestartService();
    Task StopService();
    Task StartService();

    Task<RigInfo[]> GetSupportedRigModels();

    Task<RadioData> GetAllRigInfo();

    Task<string> GetServiceVersion();

    bool GetPollingAllowed();
    int GetPollingInterval();

    Task ExecuteTest(RigBackendServiceEnum backendServiceEnum,
        ApplicationSettings draftSettings,
        CancellationToken cancellationToken);
}