using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Models;

namespace CloudlogHelper.LogService;

[JsonDerivedType(typeof(ClublogThirdPartyLogService), nameof(ClublogThirdPartyLogService))]
[JsonDerivedType(typeof(EqslThirdPartyLogService),  nameof(EqslThirdPartyLogService))]
[JsonDerivedType(typeof(HamCQThirdPartyLogService),  nameof(HamCQThirdPartyLogService))]
[JsonDerivedType(typeof(HRDLogThirdPartyLogService),   nameof(HRDLogThirdPartyLogService))]
[JsonDerivedType(typeof(LoTWThirdPartyLogService),   nameof(LoTWThirdPartyLogService))]
[JsonDerivedType(typeof(QRZThirdPartyLogService),    nameof(QRZThirdPartyLogService))]
[JsonDerivedType(typeof(QRZThirdPartyLogService),    nameof(CustomAPIThirdPartyLogService))]
public abstract class ThirdPartyLogService
{
    /// <summary>
    ///     Determines whether to upload this qso automatically.
    /// </summary>
    public virtual bool AutoQSOUploadEnabled { get; set; } = false;

    /// <summary>
    ///     Test connection of specified log service.
    /// </summary>
    public abstract Task TestConnectionAsync(CancellationToken token);

    /// <summary>
    ///     Upload qso to specified log system use customized logic.
    /// </summary>
    /// <param name="adif"></param>
    public abstract Task UploadQSOAsync(string? adif, CancellationToken token);

    public virtual Task UploadRigInfoAsync(RadioData rigData, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Preinit works. This will be called on application start.
    /// </summary>
    /// <returns></returns>
    public virtual Task PreInitAsync(CancellationToken token)
    {
        return Task.CompletedTask;
    }
}