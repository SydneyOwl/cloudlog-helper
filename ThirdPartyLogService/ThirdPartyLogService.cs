using System;
using System.Runtime;
using System.Threading.Tasks;
using ReactiveUI;

namespace CloudlogHelper.ThirdPartyLogService;

public abstract class ThirdPartyLogService
{
    /// <summary>
    /// Determines whether to upload this qso automatically.
    /// </summary>
    public virtual bool AutoQSOUploadEnabled { get; set; } = false;
    
    /// <summary>
    /// Test connection of specified log service.
    /// </summary>
    public abstract Task TestConnectionAsync();
    
    /// <summary>
    /// Upload qso to specified log system use customized logic.
    /// </summary>
    /// <param name="adif"></param>
    public abstract Task UploadQSOAsync(string? adif);
}