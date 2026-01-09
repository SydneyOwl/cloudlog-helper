using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CloudlogHelper.Database;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using Newtonsoft.Json;
using NLog;
using SQLite;

namespace CloudlogHelper.Services;

public class DatabaseService : IDatabaseService, IDisposable
{
    private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
    private SQLiteAsyncConnection? _connection;
    private bool _disposed;
    private bool _initialized;
    private Version _appVersion;
    private Version _dbVersion;
    private bool _upgradeNeeded;

    public async Task InitDatabaseAsync(string dbPath = "", bool forceInitDatabase = false)
    {
        if (_initialized) return;
        
        dbPath = string.IsNullOrEmpty(dbPath) ? DefaultConfigs.DefaultDatabaseFile : dbPath;
        var connectionString = new SQLiteConnectionString(
            dbPath,
            SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache,
            true
        );

        if (forceInitDatabase)
        {
            await TryDeleteDatabaseFileAsync(dbPath);
        }

        _connection = new SQLiteAsyncConnection(connectionString);
        _logger.Info("Creating/Migrating database...");

        await _connection.CreateTableAsync<ApplicationVersionDatabase>().ConfigureAwait(false);
        
        var dbVersionRecord = await _connection.Table<ApplicationVersionDatabase>()
            .FirstOrDefaultAsync()
            .ConfigureAwait(false) ?? ApplicationVersionDatabase.NewDefaultAppVersion();
        
        _dbVersion = new Version(dbVersionRecord.CurrentVersion!);
        _appVersion = ParseAppVersion();
        
        _upgradeNeeded = forceInitDatabase || _appVersion > _dbVersion;
        
        if (_upgradeNeeded)
        {
            _logger.Info($"Upgrade needed: {_dbVersion} => {_appVersion}");
        }
        else
        {
            _logger.Info($"Database version {_dbVersion} is up to date with app version {_appVersion}");
        }

        await _connection.EnableWriteAheadLoggingAsync().ConfigureAwait(false);
        _initialized = true;
    }

    public async Task UpgradeDatabaseAsync()
    {
        _logger.Info($"Upgrading database {_dbVersion} => {_appVersion}");
        
        var callsignCount = await _connection!.Table<CallsignDatabase>().CountAsync();
        var countryCount = await _connection!.Table<CountryDatabase>().CountAsync();
        
        await _connection!.RunInTransactionAsync(db =>
        {
            _logger.Trace($"Running upgrade transaction: {_dbVersion} => {_appVersion}");
            
            // Recreate tables
            db.DropAndCreateTable<AdifModesDatabase>();
            db.CreateTable<IgnoredQsoDatabase>();
            db.CreateTable<CollectedGridDatabase>();

            // Only reset prefix/country data if insufficient
            // 2025.1.9 cty.dat has 346 countrries and 7078 callsigns. big cty has 346 countries and 29645 callsigns.
            if (callsignCount < 3539 || countryCount < 173)
            {
                db.DropAndCreateTable<CallsignDatabase>();
                db.DropAndCreateTable<CountryDatabase>();
                InitializePrefixAndCountryData(db);
            }
            else
            {
                db.CreateTable<CallsignDatabase>();
                db.CreateTable<CountryDatabase>();
            }
            
            InitializeAdifModesDatabase(db);
            db.InsertOrReplace(ApplicationVersionDatabase.NewAppVersionWithVersionNumber(_appVersion.ToString()));
            _logger.Trace($"Upgrade transaction completed: {_dbVersion} => {_appVersion}");
        }).ConfigureAwait(false);
        
        _logger.Info("Database upgrade completed.");
    }

    public bool IsUpgradeNeeded() => _upgradeNeeded;

    public async Task<string> GetParentModeAsync(string mode)
    {
        var result = await _connection!.Table<AdifModesDatabase>()
            .Where(x => x.SubMode == mode)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        
        return result?.Mode ?? string.Empty;
    }

    public async Task<CountryDatabase> GetCallsignDetailAsync(string callsign)
    {
        if (string.IsNullOrEmpty(callsign)) return new CountryDatabase { CountryName = "Unknown", };
        
        var prefixes = new List<string>();
        var length = Math.Min(callsign.Length, 6);
        for (var i = length; i >= 1; i--)
            prefixes.Add(callsign.Substring(0, i));
        prefixes.Add($"={callsign}");
        var parameters = prefixes.Select(p => (object)p).ToArray();
        
        var placeholders = string.Join(",", Enumerable.Repeat("?", prefixes.Count));

        var query = $@"
            SELECT a.*, b.*
            FROM callsigns AS a
            LEFT JOIN countries AS b ON a.country_id = b.id
            WHERE a.callsign IN ({placeholders})
            ORDER BY LENGTH(a.callsign) DESC
            LIMIT 1";

        var results = await _connection!.QueryAsync<CountryDatabase>(query, parameters)
            .ConfigureAwait(false);
        
        return results.FirstOrDefault() ?? new CountryDatabase();
    }

    public async Task MarkQsoIgnored(IgnoredQsoDatabase ignoredQso)
    {
        try
        {
            if (!await GetIsQsoIgnoredAsync(ignoredQso).ConfigureAwait(false))
            {
                ignoredQso.UpdatedAt = DateTime.Now;
                await _connection!.InsertAsync(ignoredQso).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to mark QSO as ignored");
        }
    }

    public async Task<bool> IsQsoIgnored(IgnoredQsoDatabase ignoredQso)
    {
        try
        {
            var existingEntries = await FindIgnoredQsoAsync(ignoredQso).ConfigureAwait(false);
            if (existingEntries?.Count == 0) return false;

            return existingEntries?.Any(entry => IsSimilarIgnoredQso(entry, ignoredQso)) ?? false;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to check if QSO is ignored");
            return true;
        }
    }

    public async Task BatchAddOrUpdateCallsignGridAsync(List<CollectedGridDatabase> collectedGrid)
    {
        try
        {
            if (collectedGrid?.Count == 0) return;
            
            var parameters = new List<object>();
            var valuePlaceholders = new List<string>();
            
            foreach (var item in collectedGrid)
            {
                parameters.AddRange(new object[] { item.Callsign!, item.GridSquare!, DateTime.Now });
                valuePlaceholders.Add("(?, ?, ?)");
            }

            var query = $@"
                INSERT INTO collected_grid (callsign, grid_square, updated_at)
                VALUES {string.Join(", ", valuePlaceholders)}
                ON CONFLICT(callsign) DO UPDATE SET
                    grid_square = excluded.grid_square,
                    updated_at = excluded.updated_at";

            await _connection!.ExecuteAsync(query, parameters.ToArray()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to batch update callsign grid data");
        }
    }

    public async Task<string?> GetGridByCallsign(string callsign)
    {
        try
        {
            var result = await _connection!.Table<CollectedGridDatabase>()
                .Where(x => x.Callsign == callsign)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);
            
            return result?.GridSquare;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to get grid by callsign");
            return null;
        }
    }

    public async Task<List<IgnoredQsoDatabase>?> FindIgnoredQso(IgnoredQsoDatabase ignoredQso)
    {
        try
        {
            return await _connection!.Table<IgnoredQsoDatabase>()
                .Where(x => x.De == ignoredQso.De &&
                           x.Dx == ignoredQso.Dx &&
                           x.FinalMode == ignoredQso.FinalMode &&
                           x.RstSent == ignoredQso.RstSent &&
                           x.RstRecv == ignoredQso.RstRecv)
                .ToListAsync()
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to find ignored QSO");
            return null;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public void Dispose(bool disposing)
    {
        if (_disposed) return;
        
        if (disposing)
        {
            _connection?.CloseAsync().GetAwaiter().GetResult();
        }
        
        _disposed = true;
    }

    #region Private Helper Methods

    private Version ParseAppVersion()
    {
        var appVer = VersionInfo.Version;
        
        // Handle development version tags (e.g., "1.0.0-beta")
        var cleanVersion = appVer.Split('-')[0];
        
        try
        {
            return new Version(cleanVersion);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to parse version number");
            return new Version("0.0.0");
        }
    }

    private async Task TryDeleteDatabaseFileAsync(string dbPath)
    {
        try
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to delete existing database file");
        }
    }

    private bool IsSimilarIgnoredQso(IgnoredQsoDatabase a, IgnoredQsoDatabase b)
    {
        if (!TryParseFrequencies(a.Freq, b.Freq, out var freqA, out var freqB))
            return false;
            
        if (a.QsoStartTime is null || b.QsoStartTime is null)
            return false;

        return Math.Abs(freqB - freqA) < DefaultConfigs.AllowedFreqOffsetMHz &&
               Math.Abs((a.QsoStartTime - b.QsoStartTime).Value.TotalMinutes) < DefaultConfigs.AllowedTimeOffsetMinutes;
    }

    private bool TryParseFrequencies(string freqA, string freqB, out float parsedA, out float parsedB)
    {
        parsedA = 0;
        parsedB = 0;
        
        return float.TryParse(freqA, out parsedA) && 
               float.TryParse(freqB, out parsedB);
    }

    private async Task<bool> GetIsQsoIgnoredAsync(IgnoredQsoDatabase ignoredQso)
    {
        var existingEntries = await FindIgnoredQsoAsync(ignoredQso).ConfigureAwait(false);
        return existingEntries?.Any(entry => IsSimilarIgnoredQso(entry, ignoredQso)) ?? false;
    }

    private async Task<List<IgnoredQsoDatabase>?> FindIgnoredQsoAsync(IgnoredQsoDatabase ignoredQso)
    {
        try
        {
            return await _connection!.Table<IgnoredQsoDatabase>()
                .Where(x => x.De == ignoredQso.De &&
                           x.Dx == ignoredQso.Dx &&
                           x.FinalMode == ignoredQso.FinalMode &&
                           x.RstSent == ignoredQso.RstSent &&
                           x.RstRecv == ignoredQso.RstRecv)
                .ToListAsync()
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to find ignored QSO");
            return null;
        }
    }

    private void InitializeAdifModesDatabase(SQLiteConnection connection)
    {
        try
        {
            var json = ReadEmbeddedResourceAsString(DefaultConfigs.EmbeddedeAdifModeFilename);
            var adifModes = JsonConvert.DeserializeObject<List<AdifModesDatabase>>(json);
            
            if (adifModes?.Count > 0)
            {
                connection.InsertAll(adifModes);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to initialize ADIF modes database");
        }
    }

    private void InitializePrefixAndCountryData(SQLiteConnection connection)
    {
        try
        {
            var callsigns = new List<CallsignDatabase>();
            var countries = new List<CountryDatabase>();
            
            var ctyData = ReadEmbeddedResourceAsString(DefaultConfigs.EmbeddedCtyFilename);
            var entries = ctyData.Split(';', StringSplitOptions.RemoveEmptyEntries);
            
            for (int i = 0; i < entries.Length; i++)
            {
                if (!entries[i].Contains(':')) continue;
                
                var country = new CountryDatabase(entries[i])
                {
                    Id = i + 1
                };
                countries.Add(country);
                
                var countryPrefixes = ExtractCallsignPrefixes(entries[i]);
                foreach (var prefix in countryPrefixes)
                {
                    callsigns.Add(new CallsignDatabase
                    {
                        Callsign = prefix,
                        CountryId = i + 1
                    });
                }
            }
            
            connection.InsertAll(countries);
            connection.InsertAll(callsigns);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to initialize prefix and country data");
        }
    }

    private IEnumerable<string> ExtractCallsignPrefixes(string ctyEntry)
    {
        var parts = ctyEntry.Split(':');
        if (parts.Length < 9) return Enumerable.Empty<string>();
        
        var prefixes = parts[8].Replace("\n", "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Select(p =>
            {
                int endIndex = p.IndexOfAny(new[] { '(', '[' });
                return endIndex > 0 ? p[..endIndex] : p;
            });
        
        return prefixes;
    }

    private string ReadEmbeddedResourceAsString(string resourceName)
    {
        var stream = ApplicationStartUpUtil.GetSingleResourceStream(resourceName);
        if (stream == null)
        {
            _logger.Warn($"Embedded resource not found: {resourceName}");
            return string.Empty;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    #endregion
}

// Extension method for SQLiteConnection
internal static class SQLiteConnectionExtensions
{
    public static void DropAndCreateTable<T>(this SQLiteConnection connection) where T : new()
    {
        connection.DropTable<T>();
        connection.CreateTable<T>();
    }
}