using System.Collections.Generic;
using CloudlogHelper.LogService;
using CloudlogHelper.Models;

namespace CloudlogHelper.Services.Interfaces;

public interface ILogSystemManager
{
    ThirdPartyLogService[]? GetLogServices();

    LogSystemConfig[]? ExtractLogSystemConfigBatch(IEnumerable<ThirdPartyLogService> ls);

    void ApplyLogServiceChanges(List<ThirdPartyLogService> logServices, List<LogSystemConfig> rawConfigs);
}