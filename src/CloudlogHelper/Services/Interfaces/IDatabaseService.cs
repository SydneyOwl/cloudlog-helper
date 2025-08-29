using System.Collections.Generic;
using System.Threading.Tasks;
using CloudlogHelper.Database;

namespace CloudlogHelper.Services.Interfaces;

public interface IDatabaseService
{
    Task InitDatabaseAsync(string dbPath = "", bool forceInitDatabase = false);
    Task UpgradeDatabaseAsync();
    bool IsUpgradeNeeded();
    Task<string> GetParentModeAsync(string mode);
    Task<CountryDatabase> GetCallsignDetailAsync(string callsign);
    Task MarkQsoIgnored(IgnoredQsoDatabase ignoredQso);
    Task<bool> IsQsoIgnored(IgnoredQsoDatabase ignoredQso);
    Task AddOrUpdateCallsignGrid(CollectedGridDatabase ignoredQso);
    Task<string?> GetGridByCallsign(string callsign);
}