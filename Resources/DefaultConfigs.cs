using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CloudlogHelper.Resources;

public class DefaultConfigs
{
    /// <summary>
    ///     Repo address.
    /// </summary>
    public static readonly string RepoAddress = "https://github.com/SydneyOwl/cloudlog-helper";

    /// <summary>
    ///     Embedded resource filename for the country code mapping (English to Chinese).
    /// </summary>
    public static readonly string EmbeddedCountryEn2cnFilename = "CloudlogHelper.Resources.country_en2cn.dat";

    /// <summary>
    ///     Embedded resource filename for the CTY (Country) data file.
    /// </summary>
    public static readonly string EmbeddedCtyFilename = "CloudlogHelper.Resources.cty.dat";

    /// <summary>
    ///     Embedded resource filename for the adif_modes data file. This file comes from sql of cloudlog.
    /// </summary>
    public static readonly string EmbeddedeAdifModeFilename = "CloudlogHelper.Resources.adif_modes.dat";

    /// <summary>
    ///     Default timeout (in seconds) for HTTP(s) requests.
    /// </summary>
    public static readonly int DefaultRequestTimeout = 10;


    /// <summary>
    ///     Default timeout (in seconds) for QSODownload HTTP(s) requests.
    /// </summary>
    public static readonly int QSODownloadRequestTimeout = 30;

    /// <summary>
    ///     Timeout (in seconds) for polling Cloudlog API (CloudlogKeyStationStatisticsAPIEndpoint).
    /// </summary>
    public static readonly int CloudlogInfoPollRequestTimeout = 30;

    /// <summary>
    ///     Duration (in minutes) for which QSO data is collected to calculate average QSO metrics.
    /// </summary>
    public static int InitialQsoCalcWindowDuration = 5;

    /// <summary>
    ///     Default ua for our application.
    /// </summary>
    public static string DefaultHTTPUserAgent = $"CloudlogHelper/{VersionInfo.Version}";

    /// <summary>
    ///     Default name for our application.
    /// </summary>
    public static string DefaultApplicationName = $"CloudlogHelper {VersionInfo.Version}";

    /// <summary>
    ///     QSOSampleCountChoice
    /// </summary>
    public static List<int> QSOSampleCountChoice = new() { 50, 100, 200, 500, 1000 };

    /// <summary>
    ///     QSODayRangeChoice
    /// </summary>
    public static List<int> QSODayRangeChoice = new() { 10, 30, 60, 120, 240, 365, 720, 1000, 2000 };
    
    /// <summary>
    ///     API endpoint for Checking whether this is a wavelog instance or not (by checking manifest.json)
    ///     Maybe someday I'll call it cloudwave helper. haha!
    /// </summary>
    public static string CloudOrWaveCheckEndpoint = "/manifest.json";

    /// <summary>
    ///     API endpoint for interacting with QSO (log entry) data in Cloudlog.
    /// </summary>
    public static string CloudlogQSOAPIEndpoint = "/index.php/api/qso/";

    /// <summary>
    ///     API endpoint for login Cloudlog.
    /// </summary>
    public static string CloudlogLoginEndpoint = "/index.php/user/login";

    /// <summary>
    ///     API endpoint for Cloudlog settings.
    /// </summary>
    public static string CloudlogDashboardEndpoint = "/index.php/dashboard";

    /// <summary>
    ///     API endpoint for Cloudlog settings.
    /// </summary>
    public static string CloudlogAdifFileUploadEndpoint = "/index.php/adif/import";

    /// <summary>
    ///     API endpoint for fetching recent qsos.
    /// </summary>
    public static string CloudlogQSOAdvancedEndpoint = "/index.php/logbookadvanced/search";

    /// <summary>
    ///     API Endpoint for downloading adif in specified date range.
    /// </summary>
    public static string ExportCustomAdifLogs = "/adif/export_custom";

    /// <summary>
    ///     API endpoint for testing authentication keys in Cloudlog.
    /// </summary>
    public static readonly string CloudlogTestAPIEndpoint = "/index.php/api/auth/";

    /// <summary>
    ///     API endpoint for retrieving station information from Cloudlog.
    /// </summary>
    public static readonly string CloudlogStationInfoAPIEndpoint = "/index.php/api/station_info/";

    /// <summary>
    ///     API endpoint for retrieving station statistics from Cloudlog.
    /// </summary>
    public static readonly string CloudlogStationStatisticsAPIEndpoint = "/index.php/api/statistics/";

    /// <summary>
    ///     API endpoint for standard radio API calls(version 2) in Cloudlog.
    /// </summary>
    public static readonly string CloudlogRadioAPICallV2Endpoint = "/index.php/api/radio/";

    /// <summary>
    ///     Api endpoint for clublog
    /// </summary>
    public static readonly string ClublogQsoUploadEndpoint = "https://clublog.org/realtime.php";

    /// <summary>
    ///     Test Api endpoint for clublog
    /// </summary>
    public static readonly string ClublogTestEndpoint = "https://clublog.org/getadif.php";

    /// <summary>
    ///     QSO Upload api endpoint for hamcq
    /// </summary>
    public static readonly string HamCQQsoUploadEndpoint = "https://api.hamcq.cn/v1/logbook?from=gridtracker";

    /// <summary>
    ///     QSO test api endpoint for eqsl
    /// </summary>
    public static readonly string EqslTestEndpoint =
        "https://www.eQSL.cc/qslcard/DownloadInBox.cfm";
    
    /// <summary>
    ///     QSO Upload api endpoint for eqsl
    /// </summary>
    public static readonly string EqslQsoUploadEndpoint =
        "https://www.eQSL.cc/qslcard/importADIF.cfm";

    /// <summary>
    ///     This is for cloudlog helper only.
    ///     See also https://clublog.freshdesk.com/support/solutions/articles/54910-api-keys
    /// </summary>
    public static readonly string Clkk = ("afce|ce|4" + "6e95|||83d99af3|9851" + "429d18dade6670278").Replace("|", "");

    /// <summary>
    /// Allowed freq offset when comparing qso.
    /// </summary>
    public static readonly float AllowedFreqOffsetMHz = 0.1f;
    
    /// <summary>
    /// Allowed time offset when comparing qso.
    /// </summary>
    public static readonly int AllowedTimeOffsetMinutes = 10;
    
    /// <summary>
    ///     Default host address for Rigctld.
    /// </summary>
    public static readonly string RigctldDefaultHost = "127.0.0.1";

    /// <summary>
    ///     Default host address for Outside Rigctld.
    /// </summary>
    public static readonly string RigctldExternalHost = "127.0.0.1:4534";

    /// <summary>
    ///     Default port number for Rigctld.
    /// </summary>
    public static readonly int RigctldDefaultPort = 4534;

    /// <summary>
    ///     Default poll interval for Rigctld.
    /// </summary>
    public static readonly int RigctldDefaultPollingInterval = 15;

    /// <summary>
    ///     The maximum number of output lines to retain from rigctld.
    ///     When rigctld exits, the most recent MaxRigctldOutputLines lines will be logged.
    /// </summary>
    /// <remarks>
    ///     - Set to 0 to disable logging rigctld output.
    ///     - Set to -1 to continuously rediect stdout and stderr of rigctld to console.
    /// </remarks>
    public static readonly int MaxRigctldOutputLineCount = 0;

    /// <summary>
    ///     Default port number of proxy server.
    /// </summary>
    public static readonly string DebugServerDefaultBindingAddress = "127.0.0.1:7584";

    /// <summary>
    ///     Socket timeout (in milliseconds) for Rigctld communication.
    /// </summary>
    public static readonly int RigctldSocketTimeout = 2500;

    /// <summary>
    ///     The default expiration time for the cached return values of the rigctld In seconds.
    ///     This is calculated dynamically when jtdx sending requests.
    /// </summary>
    // public static readonly int RigctldCacheExpirationTime = 8;

    /// <summary>
    ///     Default arguments passed to Rigctld when starting the process.
    ///     I think it should be safe to disable RTS and DTR since they are typically used for PTT control,
    ///     but this software only reads frequency and mode information. Additionally, on Linux, my radio defaults
    ///     to accidentally triggering TX due to RTS/DTR signals, so I’ve disabled them in the default configuration.
    /// </summary>
    // public static readonly string RigctldDefaultArgs =
    //     $@"--set-conf=""rts_state=OFF"" --set-conf ""dtr_state=OFF"" -T {RigctldDefaultHost} -t {RigctldDefaultPort} -vvvvv";

    /// <summary>
    ///     A list of supported radio modes for Rigctld.
    /// </summary>
    public static readonly List<string> AvailableRigModes = new()
    {
        "AM", "CW", "USB", "LSB", "FM", "CWR", "PKTLSB", "PKTUSB",
        "RTTY", "RTTYR", "WFM", "AMS", "PKTFM", "ECSSUSB", "ECSSLSB", "FA", "SAM", "SAL", "SAH", "DSB"
    };

    /// <summary>
    ///     A list of processes that may conflict with Cloudlog Helper.
    /// </summary>
    public static readonly List<string> PossibleRigctldConfilcts = new() { "jtdx", "wsjt", "rigctl", "fldigi" };


    /// <summary>
    ///     Maximum error count of rigctld communication. A msgbox will popup If exceeded.
    /// </summary>
    public static readonly int MaxRigctldErrorCount = 3;

    /// <summary>
    ///     Expiry time (in seconds) for UDP client connections.
    /// </summary>
    public static readonly int UDPClientExpiryInSeconds = 60;

    public static readonly int UDPServerDefaultPort = 2237;

    /// <summary>
    ///     Default path to SQLite database file.
    /// </summary>
    public static string DefaultDatabaseFile => Path.Join(AppContext.BaseDirectory, "cloudlog-helper.db");

    /// <summary>
    ///     Default path to the settings file.
    /// </summary>
    public static string DefaultSettingsFile => Path.Join(AppContext.BaseDirectory, "settings.json");

    /// <summary>
    ///     Path to Rigctld executable file.
    /// </summary>
    public static string ExecutableRigctldPath => Path.Join(AppContext.BaseDirectory,
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "rigctld.exe" : "rigctld");
}