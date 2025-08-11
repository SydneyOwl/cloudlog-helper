using System.Collections.Generic;
using System.Threading.Tasks;
using CloudlogHelper.Database;

namespace CloudlogHelper.Services;

public interface IDatabaseService
{
    Task InitDatabaseAsync(string dbPath = "", bool forceInitDatabase = false);
    Task<string> GetParentModeAsync(string mode);
    Task<CountryDatabase> GetCallsignDetailAsync(string callsign);
    Task MarkQsoIgnored(IgnoredQsoDatabase ignoredQso);
    Task<bool> IsQsoIgnored(IgnoredQsoDatabase ignoredQso);
    Task<List<IgnoredQsoDatabase>?> FindIgnoredQso(IgnoredQsoDatabase ignoredQso);
}