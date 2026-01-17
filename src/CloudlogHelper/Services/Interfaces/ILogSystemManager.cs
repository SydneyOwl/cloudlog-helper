using System.Collections.Generic;
using System.Threading.Tasks;
using CloudlogHelper.LogService;
using CloudlogHelper.Models;

namespace CloudlogHelper.Services.Interfaces;

public interface ILogSystemManager
{
    ThirdPartyLogService[]? GetEmptySupportedLogServices();

    Task PreInitLogSystem(IEnumerable<ThirdPartyLogService> ls);

    LogSystemConfig[]? ExtractLogSystemConfigBatch(IEnumerable<ThirdPartyLogService> ls);

    void ApplyLogServiceChanges(List<ThirdPartyLogService> logServices, List<LogSystemConfig> rawConfigs);
}