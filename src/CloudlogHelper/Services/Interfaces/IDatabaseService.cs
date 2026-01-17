using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CloudlogHelper.Database;

namespace CloudlogHelper.Services.Interfaces;

public interface IDatabaseService
{
    Task InitDatabaseAsync(string dbPath = "", bool forceInitDatabase = false);
    Task UpgradeDatabaseAsync();
    bool IsUpgradeNeeded();
    Version GetVersionBeforeUpdate();
    Task<string> GetParentModeAsync(string mode);
    Task<CountryDatabase> GetCallsignDetailAsync(string callsign);
    Task MarkQsoIgnored(IgnoredQsoDatabase ignoredQso);
    Task<bool> IsQsoIgnored(IgnoredQsoDatabase ignoredQso);
    Task BatchAddOrUpdateCallsignGridAsync(List<CollectedGridDatabase> collectedGrid);
    Task<string?> GetGridByCallsign(string callsign);
    Task<(int, int)> UpdateCallsignAndCountry(string ctyDat);
}