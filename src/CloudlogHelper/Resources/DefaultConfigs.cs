using System;
using System.Collections.Generic;
using System.IO;
using CloudlogHelper.Utils;

namespace CloudlogHelper.Resources;

public class DefaultConfigs
{
    public const string MutexId = "{A5E98F80-B924-4CFF-A370-C992FDCC833D}";

    /// <summary>
    ///     Repo address.
    /// </summary>
    public const string RepoAddress = "https://github.com/SydneyOwl/cloudlog-helper";

    /// <summary>
    ///     Embedded resource filename for the CTY (Country) data file.
    /// </summary>
    public const string EmbeddedCtyFilename = "cty.dat";

    /// <summary>
    ///     Embedded resource filename for the adif_modes data file. This file comes from sql of cloudlog.
    /// </summary>
    public const string EmbeddedeAdifModeFilename = "adif_modes.dat";

    /// <summary>
    ///     Default timeout (in seconds) for HTTP(s) requests.
    /// </summary>
    public const int DefaultRequestTimeout = 10;

    /// <summary>
    ///     Default timeout (in seconds) for HTTP(s) forwarding requests.
    /// </summary>
    public const int DefaultForwardingRequestTimeout = 3;

    /// <summary>
    ///     Default timeout (in seconds) for QSODownload HTTP(s) requests.
    /// </summary>
    public const int QSODownloadRequestTimeout = 30;

    /// <summary>
    ///     Timeout (in seconds) for polling Cloudlog API (CloudlogKeyStationStatisticsAPIEndpoint).
    /// </summary>
    public const int CloudlogInfoPollRequestTimeout = 30;

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

    public static string DefaultApplicationNameNoVersion = "CloudlogHelper";

    /// <summary>
    ///     QSOSampleCountChoice
    /// </summary>
    public static List<int> QSOSampleCountChoice = new() { 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 };

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
    public const string CloudlogTestAPIEndpoint = "/index.php/api/auth/";

    /// <summary>
    ///     API endpoint for retrieving station information from Cloudlog.
    /// </summary>
    public const string CloudlogStationInfoAPIEndpoint = "/index.php/api/station_info/";

    /// <summary>
    ///     API endpoint for retrieving station statistics from Cloudlog.
    /// </summary>
    public const string CloudlogStationStatisticsAPIEndpoint = "/index.php/api/statistics/";

    /// <summary>
    ///     API endpoint for standard radio API calls(version 2) in Cloudlog.
    /// </summary>
    public const string CloudlogRadioAPICallV2Endpoint = "/index.php/api/radio/";

    /// <summary>
    ///     Api endpoint for clublog
    /// </summary>
    public const string ClublogQsoUploadEndpoint = "https://clublog.org/realtime.php";

    /// <summary>
    ///     Test Api endpoint for clublog
    /// </summary>
    public const string ClublogTestEndpoint = "https://clublog.org/getadif.php";

    /// <summary>
    ///     QSO Upload api endpoint for hamcq
    /// </summary>
    public const string HamCQQsoUploadEndpoint = "https://api.hamcq.cn/v1/logbook?from=gridtracker";

    /// <summary>
    ///     QSO test api endpoint for eqsl
    /// </summary>
    public const string EqslTestEndpoint =
        "https://www.eQSL.cc/qslcard/DownloadInBox.cfm";

    /// <summary>
    ///     QSO Upload api endpoint for eqsl
    /// </summary>
    public const string EqslQsoUploadEndpoint =
        "https://www.eQSL.cc/qslcard/importADIF.cfm";

    /// <summary>
    ///     This is for cloudlog helper only.
    ///     See also https://clublog.freshdesk.com/support/solutions/articles/54910-api-keys
    /// </summary>
    public static readonly string Clkk = ("afce|ce|4" + "6e95|||83d99af3|9851" + "429d18dade6670278").Replace("|", "");

    /// <summary>
    ///     Allowed freq offset when comparing qso.
    /// </summary>
    public const float AllowedFreqOffsetMHz = 0.1f;

    /// <summary>
    ///     Allowed time offset when comparing qso.
    /// </summary>
    public const int AllowedTimeOffsetMinutes = 10;

    /// <summary>
    ///     Default host address for flrig.
    /// </summary>
    public const string FLRigDefaultHost = "127.0.0.1";

    /// <summary>
    ///     Default host address for flrig.
    /// </summary>
    public const string FLRigDefaultPort = "12345";
    
    
    public const string OmniRigEngineProgId = "OmniRig.OmniRigX";
    
    public static List<string> OmniRigAvailableRig = new() {"Rig 1", "Rig 2"};

    /// <summary>
    ///     Default host address for Rigctld.
    /// </summary>
    public const string RigctldDefaultHost = "127.0.0.1";

    /// <summary>
    ///     Default host address for Outside Rigctld.
    /// </summary>
    public const string RigctldExternalHost = "127.0.0.1:4534";

    /// <summary>
    ///     Default port number for Rigctld.
    /// </summary>
    public const int RigctldDefaultPort = 4534;

    /// <summary>
    ///     Default poll interval for Rigctld.
    /// </summary>
    public const int RigDefaultPollingInterval = 15;

    /// <summary>
    ///     The maximum number of output lines to retain from rigctld.
    ///     When rigctld exits, the most recent MaxRigctldOutputLines lines will be logged.
    /// </summary>
    /// <remarks>
    ///     - Set to 0 to disable logging rigctld output.
    ///     - Set to -1 to continuously rediect stdout and stderr of rigctld to console.
    /// </remarks>
    public const int MaxRigctldOutputLineCount = 0;

    /// <summary>
    ///     Default port number of proxy server.
    /// </summary>
    public const string DebugServerDefaultBindingAddress = "127.0.0.1:7584";

    /// <summary>
    ///     Socket timeout (in milliseconds) for Rigctld communication.
    /// </summary>
    public const int RigctldSocketTimeout = 2500;

    /// <summary>
    ///     The default expiration time for the cached return values of the rigctld In seconds.
    ///     This is calculated dynamically when jtdx sending requests.
    /// </summary>
    // public const int RigctldCacheExpirationTime = 8;

    /// <summary>
    ///     Default arguments passed to Rigctld when starting the process.
    ///     I think it should be safe to disable RTS and DTR since they are typically used for PTT control,
    ///     but this software only reads frequency and mode information. Additionally, on Linux, my radio defaults
    ///     to accidentally triggering TX due to RTS/DTR signals, so I’ve disabled them in the default configuration.
    /// </summary>
    // public const string RigctldDefaultArgs =
    //     $@"--set-conf=""rts_state=OFF"" --set-conf ""dtr_state=OFF"" -T {RigctldDefaultHost} -t {RigctldDefaultPort} -vvvvv";

    /// <summary>
    ///     A list of supported radio modes.
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
    public static int MaxRigctldErrorCount = 3;

    public static int ExportedPolarChartSize = 600;

    /// <summary>
    ///     Expiry time (in seconds) for UDP client connections.
    /// </summary>
    public const int UDPClientExpiryInSeconds = 60;

    public const int UDPServerDefaultPort = 2237;

    public const int DefaultBatchProcessGridMessageInterval = 18;
    public const int DefaultNotificationTimeout = 5;
    public const int DefaulPolarKValue = 5;
    public const double DefaulPolarAngWeightValue = 0.5;
    public const double DefaulPolarDistWeightValue = 0.5;
    public const int DefaultPolarQSOSamples = 1000;
    public const int DefaultHistogramQSOSamples = 9000;

    /// <summary>
    ///     Default cache size for qso points.
    /// </summary>
    public const int DefaultChartDataCacheNumber = 15000;

    public const int UpdateChartsThrottleTime = 3;

    public const int CLHTCPConnRetryDelayMs = 3000;

    public const int CLHHeartbeatTimeoutSec = 16;
    public const int CLHHeartbeatIntervalMilliSec = 5000;

    /// <summary>
    ///     Default timeout (in seconds) for start/stop.
    /// </summary>
    public const int DefaultProcessTPStartStopTimeout = 5;

    /// <summary>
    ///     Default Hamlib files for windows
    /// </summary>
    public static List<string> DefaultWindowsHamlibFiles = new()
    {
        "libgcc_s_dw2-1.dll",
        "libgcc_s_seh-1.dll",
        "libhamlib-4.dll",
        "libusb-1.0.dll",
        "libwinpthread-1.dll",
        "rigctld.exe"
    };

    /// <summary>
    ///     Default Hamlib files for windows
    /// </summary>
    public static List<string> DefaultLinuxHamlibFiles = new()
    {
        "rigctld"
    };

    public static string HamlibFilePath = Path.Join(
        ApplicationStartUpUtil.GetConfigDir(),
        "hamlib");

    public const int LogServicePreinitTimeoutSec = 5;

    /// <summary>
    ///     Default World map
    /// </summary>
    public static string DefaultWorldMapFile = "NE2_50M_SR_W_proc_small.jpg";

    public static int WorldHeatmapWidth = 360;
    public static int WorldHeatmapHeight = 180;

    public static string DefaultTempFilePath = Path.Join(Path.GetTempPath(), "CloudlogHelperTmpFiles");

    /// <summary>
    ///     Default path to SQLite database file.
    /// </summary>
    public static string DefaultDatabaseFile => Path.Join(ApplicationStartUpUtil.GetConfigDir(), "cloudlog-helper.db");

    /// <summary>
    ///     Default path to the settings file.
    /// </summary>
    public static string DefaultSettingsFile => Path.Join(ApplicationStartUpUtil.GetConfigDir(), "settings.json");

    /// <summary>
    ///     Path to Rigctld executable file.
    /// </summary>
    public static string ExecutableRigctldPath => Path.Join(
        HamlibFilePath,
        OperatingSystem.IsWindows() ? "rigctld.exe" : "rigctld");
}