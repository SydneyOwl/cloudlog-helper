using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CloudlogHelper.Database;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using Newtonsoft.Json;
using NLog;
using SQLite;

namespace CloudlogHelper.Services;

public class DatabaseService : IDatabaseService, IDisposable
{
    /// <summary>
    ///     Connection to the sqlite database.
    /// </summary>
    private SQLiteAsyncConnection? _conn;
    
    /// <summary>
    ///     Map English country names to Chinese. Only used when initializing database.
    /// </summary>
    private Dictionary<string, string> _countries = new();

    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private readonly ILogger ClassLogger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Whether this is inited or not.
    /// </summary>
    private bool _inited;
    
    /// <summary>
    /// Whether this is disposed or not.
    /// </summary>
    private bool _disposed;

    /// <summary>
    ///     Check if database is already initialized; if not, init it.
    /// </summary>
    /// <param name="dbPath"></param>
    /// <param name="forceInitDatabase"></param>
    public async Task InitDatabaseAsync(string dbPath = "", bool forceInitDatabase = false)
    {
        if (_inited) return;
        dbPath = string.IsNullOrEmpty(dbPath) ? DefaultConfigs.DefaultDatabaseFile : dbPath;
        var connectionString = new SQLiteConnectionString(dbPath,
            SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite |
            SQLiteOpenFlags.SharedCache, true);
        try
        {
            if (forceInitDatabase)
                try
                {
                    File.Delete(dbPath);
                }
                catch
                {
                    // ignored...
                }

            _conn = new SQLiteAsyncConnection(connectionString);
            ClassLogger.Info("Creating/Migrating database...");

            // Check if version number exists
            await _conn.CreateTableAsync<ApplicationVersionDatabase>();
            var dbVer = await _conn.Table<ApplicationVersionDatabase>().FirstOrDefaultAsync() ??
                        ApplicationVersionDatabase.NewDefaultAppVersion();
            var dbVersion = new Version(dbVer.CurrentVersion!);

            // NEVER USE `Assembly.GetEntryAssembly()?.GetName().Version`: SEEMS LIKE IT'LL CHANGE AFTER AVALONIA FULLY INITIALIZED!
            var appVer = VersionInfo.Version;
            var formalRelease = true;
            // for xxx-rc1
            if (appVer.Contains('-'))
            {
                formalRelease = false;
                appVer = appVer.Split("-").FirstOrDefault();
            }

            var appVersion = new Version("0.0.0");
            if (!forceInitDatabase) appVersion = new Version(appVer);
            ClassLogger.Trace($"DBVer:{dbVersion}");
            ClassLogger.Trace($"appVersion:{appVersion}");

            if (appVersion > dbVersion || forceInitDatabase || !formalRelease)
            {
                ClassLogger.Trace($"upgrading {dbVersion} => {appVersion}");
                await InitCountryDicAsync();
                await _conn.RunInTransactionAsync(db =>
                {
                    ClassLogger.Trace($"Running transaction: {dbVersion} => {appVersion}");
                    // just truncate here
                    // country id is (maybe) not fixed here; so we just truncate it. won't take much time!
                    db.DropTable<CallsignDatabase>();
                    db.DropTable<CountryDatabase>();
                    db.DropTable<AdifModesDatabase>();

                    db.CreateTable<CallsignDatabase>();
                    db.CreateTable<CountryDatabase>();
                    db.CreateTable<AdifModesDatabase>();
                    db.CreateTable<IgnoredQsoDatabase>();

                    InitPrefixAndCountryData(db);
                    InitAdifModesDatabase(db);
                    db.InsertOrReplace(
                        ApplicationVersionDatabase.NewAppVersionWithVersionNumber(appVersion.ToString()));
                    ClassLogger.Trace($"Tansaction done: {dbVersion} => {appVersion}");
                });
            }
            else
            {
                ClassLogger.Info(
                    $"Current app version is same of less then db version: {dbVersion} => {appVersion}. Creating/Migrating skipped");
            }

            ClassLogger.Info("Creating/Migrating done.");
            await _conn.EnableWriteAheadLoggingAsync();
        }
        catch (Exception e)
        {
            ClassLogger.Warn(e, "Failed to initialize database. Ignored.");
        }
        _inited = true;
    }

    /// <summary>
    ///     Check if specified mode is a submode (e.g. one of MFSK's submodes is FT4), if so, returns the name of its parent.
    /// </summary>
    /// <param name="mode"></param>
    /// <returns></returns>
    public async Task<string> GetParentModeAsync(string mode)
    {
        var quRes = await _conn!.Table<AdifModesDatabase>().Where(x => x.SubMode == mode).FirstOrDefaultAsync();
        if (quRes is null) return string.Empty;
        var md = quRes.Mode;
        return string.IsNullOrEmpty(md) ? string.Empty : md;
    }

    /// <summary>
    ///     Query country info of specified callsign.
    ///     sql come from ft8cn/app/src/main/java/com/bg7yoz/ft8cn/callsign/CallsignDatabase.java.
    /// </summary>
    /// <param name="callsign"></param>
    /// <returns></returns>
    public async Task<CountryDatabase> GetCallsignDetailAsync(string callsign)
    {
        var countriesRes = await _conn!.QueryAsync<CountryDatabase>(
            @"SELECT a.*, b.*
              FROM callsigns AS a
              LEFT JOIN countries AS b ON a.country_id =b.id
              WHERE (SUBSTR(?, 1, LENGTH(callsign))=callsign)
              OR (callsign='='||?)
              ORDER BY LENGTH(callsign) DESC
              LIMIT 1",
            callsign, callsign);
        return countriesRes.Count == 0 ? new CountryDatabase() : countriesRes[0];
    }
    
    /// <summary>
    ///     Init _countries dict.
    /// </summary>
    /// <returns></returns>
    private Task InitCountryDicAsync()
    {
        try
        {
            var co_en2cn = ReadEmbeddedFileAsString(DefaultConfigs.EmbeddedCountryEn2cnFilename);
            var st = co_en2cn.Split("\n");
            _countries = new Dictionary<string, string>();
            foreach (var t in st)
            {
                if (!t.Contains(":")) continue;
                var cc = t.Split(":");
                // Console.WriteLine($"{cc[0]} => {cc[1]}");
                _countries[cc[0]] = cc[1];
            }
        }
        catch (IOException e)
        {
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Get corrsponding english name by providing chinese.
    /// </summary>
    /// <param name="country"></param>
    /// <returns></returns>
    private string SearchEnForCountryNameCn(string country)
    {
        return _countries.TryGetValue(country, out var cnCountry) ? cnCountry : string.Empty;
    }

    /// <summary>
    ///     Init adif modes
    /// </summary>
    /// <param name="conn"></param>
    private void InitAdifModesDatabase(SQLiteConnection conn)
    {
        try
        {
            var result = ReadEmbeddedFileAsString(DefaultConfigs.EmbeddedeAdifModeFilename);
            var adifModes = JsonConvert.DeserializeObject<List<AdifModesDatabase>>(result);
            conn.InsertAll(adifModes);
        }
        catch (Exception e)
        {
            ClassLogger.Warn(e, "Failed to InitAdifModesDatabase. Ignored.");
        }
    }

    /// <summary>
    ///     Init prefix data
    /// </summary>
    private void InitPrefixAndCountryData(SQLiteConnection conn)
    {
        try
        {
            var callsigns = new List<CallsignDatabase>();
            var countries = new List<CountryDatabase>();

            var result = ReadEmbeddedFileAsString(DefaultConfigs.EmbeddedCtyFilename);
            var st = result.Split(";");
            for (var i = 0; i < st.Length; i++)
            {
                if (!st[i].Contains(":")) continue;
                var cdb = new CountryDatabase(st[i]);
                cdb.CountryNameCn = SearchEnForCountryNameCn(cdb.CountryNameEn);
                cdb.Id = i + 1;

                countries.Add(cdb);
                // await _conn!.InsertAsync(cdb);
                // calculate callsig

                # region callsign

                if (!st[i].Contains(":")) continue;
                var info = st[i].Split(":");
                if (info.Length < 9) continue;
                var ls = info[8].Replace("\n", "").Split(",");
                // await _conn.RunInTransactionAsync(tran =>
                // {
                for (var j = 0; j < ls.Length; j++)
                {
                    if (ls[j].Contains(")")) ls[j] = ls[j].Substring(0, ls[j].IndexOf("("));
                    if (ls[j].Contains("[")) ls[j] = ls[j].Substring(0, ls[j].IndexOf("["));
                    callsigns.Add(new CallsignDatabase
                    {
                        Callsign = ls[j].Trim(),
                        CountryId = i + 1
                    });
                }
                // });

                # endregion

                // _db.InsertAsync(cdb).GetAwaiter().GetResult();
            }

            conn.InsertAll(countries);
            conn.InsertAll(callsigns);
        }
        catch (Exception e)
        {
            ClassLogger.Warn(e, "Failed to InitPrefixAndCountryDataAsync. Ignored.");
            // Console.WriteLine(e.Message);
            //ignored
        }
    }

    /// <summary>
    ///     This loads embeddedresources as string.
    /// </summary>
    /// <param name="resourcePath"></param>
    /// <returns></returns>
    private string ReadEmbeddedFileAsString(string resourcePath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourcePath);
        if (stream == null)
        {
            ClassLogger.Warn("Embeeded file is null: {msg}", resourcePath);
            return "";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public async Task MarkQsoIgnored(IgnoredQsoDatabase ignoredQso)
    {
        try
        {
            if (!await IsQsoIgnored(ignoredQso))
            {
                await _conn!.InsertAsync(ignoredQso);
            }
        }
        catch (Exception e)
        {
            ClassLogger.Warn(e);
        }
    }

    /// <summary>
    /// check if qso is marked as ignored.
    /// </summary>
    /// <param name="ignoredQso"></param>
    public async Task<bool> IsQsoIgnored(IgnoredQsoDatabase ignoredQso)
    {
        try
        {
            var result = await FindIgnoredQso(ignoredQso);
            if (result is null) return true;
            if (result.Count != 0)
            {
                ClassLogger.Trace("We found the same ones!!!");
                foreach (var qIgnoredQsoDatabase in result)
                {
                    ClassLogger.Trace(qIgnoredQsoDatabase.ToString());
                    if (!float.TryParse(qIgnoredQsoDatabase.Freq, out var fA)) continue;
                    if (!float.TryParse(ignoredQso.Freq, out var fB)) continue;
                    if (qIgnoredQsoDatabase.QsoStartTime is null) continue;
                    if (ignoredQso.QsoStartTime is null) continue;
                    if ((Math.Abs(fB - fA) < DefaultConfigs.AllowedFreqOffsetMHz) &&
                        (qIgnoredQsoDatabase.QsoStartTime - ignoredQso.QsoStartTime)!.Value.Minutes <
                        DefaultConfigs.AllowedTimeOffsetMinutes)
                    {
                        ClassLogger.Info("Found same ignored qso. skipping...");
                        return true;
                    }
                }
            }

            return false;
        }
        catch (Exception e)
        {
            ClassLogger.Warn(e,"Failed to ignore.");
            return true;
        }
    }

    public async Task<List<IgnoredQsoDatabase>?> FindIgnoredQso(IgnoredQsoDatabase ignoredQso)
    {
        try
        {
            var res = await _conn!.Table<IgnoredQsoDatabase>()
                .Where(x => x.De == ignoredQso.De)
                .Where(x => x.Dx == ignoredQso.Dx)
                .Where(x => x.FinalMode == ignoredQso.FinalMode)
                .Where(x => x.RstSent == ignoredQso.RstSent)
                .Where(x => x.RstRecv == ignoredQso.RstRecv)
                .ToListAsync();
            return res;
        }
        catch (Exception e)
        {
            ClassLogger.Warn(e,"Failed to ignore.");
            return null;
        }
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _conn?.CloseAsync().GetAwaiter().GetResult();
        }
        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}