using System.Threading;
using System.Threading.Tasks;

namespace CloudlogHelper.LogService;

public abstract class ThirdPartyLogService
{
    /// <summary>
    /// Determines whether to upload this qso automatically.
    /// </summary>
    public virtual bool AutoQSOUploadEnabled { get; set; } = false;
    
    /// <summary>
    /// Test connection of specified log service.
    /// </summary>
    public abstract Task TestConnectionAsync(CancellationToken token);
    
    /// <summary>
    /// Upload qso to specified log system use customized logic.
    /// </summary>
    /// <param name="adif"></param>
    public abstract Task UploadQSOAsync(string? adif, CancellationToken token);
}