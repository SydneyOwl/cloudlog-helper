using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.Models;

namespace CloudlogHelper.Services.Interfaces;

public interface IRigService
{
    /// <summary>
    ///     Returns a unique name of this service
    /// </summary>
    /// <returns></returns>
    RigBackendServiceEnum GetServiceType();

    /// <summary>
    ///     Start current service.
    /// </summary>
    /// <param name="token"></param>
    /// <param name="args"></param>
    /// <param name="ignoreIfRunning"></param>
    /// <param name="timeoutMilliseconds"></param>
    /// <returns></returns>
    Task StartService(CancellationToken token, params object[] args);

    /// <summary>
    ///     Restart current service.
    /// </summary>
    /// <param name="args"></param>
    /// <param name="ignoreIfRunning"></param>
    /// <param name="timeoutMilliseconds"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task StopService(CancellationToken token);

    /// <summary>
    ///     Is service running
    /// </summary>
    /// <returns></returns>
    bool IsServiceRunning();

    /// <summary>
    ///     Get supported rig models
    /// </summary>
    /// <returns></returns>
    Task<List<RigInfo>> GetSupportedRigModels();

    /// <summary>
    ///     Get service version
    /// </summary>
    /// <returns></returns>
    Task<string> GetServiceVersion(params object[] args);

    /// <summary>
    ///     Query realtime riginfo
    /// </summary>
    /// <param name="reportRfPower"></param>
    /// <param name="reportSplitInfo"></param>
    /// <param name="token"></param>
    /// <param name="args"></param>
    /// <returns></returns>
    Task<RadioData> GetAllRigInfo(bool reportRfPower, bool reportSplitInfo,
        CancellationToken token, params object[] args);
}