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
    ///     Interval (in seconds) for polling Cloudlog API (CloudlogKeyStationStatisticsAPIEndpoint).
    /// </summary>
    public const int CloudlogInfoPollInterval = 30;

    /// <summary>
    ///     Timeout (in seconds) for polling Cloudlog API (CloudlogKeyStationStatisticsAPIEndpoint).
    /// </summary>
    public const int CloudlogInfoPollTimeout = 10;

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

    /// <summary>
    ///     ProgID of OmniRig (Windows COM).
    /// </summary>
    public const string OmniRigEngineProgId = "OmniRig.OmniRigX";

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
    ///     Expiry time (in seconds) for UDP client connections.
    /// </summary>
    public const int UDPClientExpiryInSeconds = 60;

    /// <summary>
    ///     Default port of UDP Server.
    /// </summary>
    public const int UDPServerDefaultPort = 2237;

    /// <summary>
    ///     Default K value (KNN) for polar chart.
    /// </summary>
    public const int DefaulPolarKValue = 5;

    /// <summary>
    ///     Default angle weight value (KNN) for polar chart.
    /// </summary>
    public const double DefaulPolarAngWeightValue = 0.5;

    /// <summary>
    ///     Default distance weight value (KNN) for polar chart.
    /// </summary>
    public const double DefaulPolarDistWeightValue = 0.5;

    /// <summary>
    ///     Default sample count for polar chart.
    /// </summary>
    public const int DefaultPolarQSOSamples = 1000;

    /// <summary>
    ///     Default cache size for qso points.
    /// </summary>
    public const int DefaultChartDataCacheNumber = 15000;

    /// <summary>
    ///     Charts update throttle secs.
    /// </summary>
    public const int UpdateChartsThrottleSec = 3;

    /// <summary>
    ///     Retry delay of CLH Service.
    /// </summary>
    public const int CLHTCPConnRetryDelayMs = 3000;

    /// <summary>
    ///     Interval btw sending HB Packets.
    /// </summary>
    public const int CLHHeartbeatIntervalMilliSec = 5000;

    /// <summary>
    ///     Default timeout (in seconds) for start/stop.
    /// </summary>
    public const int DefaultProcessTPStartStopTimeout = 5;

    /// <summary>
    ///     Timeout of pre-initing rig services.
    /// </summary>
    public const int LogServicePreinitTimeoutSec = 5;

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
    public static List<int> QSOSampleCountChoice = new() { 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 };

    /// <summary>
    ///     QSODayRangeChoice
    /// </summary>
    public static List<int> QSODayRangeChoice = new() { 10, 30, 60, 120, 240, 365, 720, 1000, 2000 };

    /// <summary>
    ///     API endpoint for Checking whether this is a wavelog instance or not (by checking manifest.json)
    ///     Maybe someday I'll call it cloudwave helper. haha!
    /// </summary>
    public const string CloudOrWaveCheckEndpoint = "/manifest.json";

    /// <summary>
    ///     API endpoint for interacting with QSO (log entry) data in Cloudlog.
    /// </summary>
    public const string CloudlogQSOAPIEndpoint = "/index.php/api/qso/";

    /// <summary>
    ///     API endpoint for login Cloudlog.
    /// </summary>
    public const string CloudlogLoginEndpoint = "/index.php/user/login";

    /// <summary>
    ///     API endpoint for Cloudlog settings.
    /// </summary>
    public const string CloudlogDashboardEndpoint = "/index.php/dashboard";

    /// <summary>
    ///     API endpoint for Cloudlog settings.
    /// </summary>
    public const string CloudlogAdifFileUploadEndpoint = "/index.php/adif/import";

    /// <summary>
    ///     API endpoint for fetching recent qsos.
    /// </summary>
    public const string CloudlogQSOAdvancedEndpoint = "/index.php/logbookadvanced/search";

    /// <summary>
    ///  Timeout of QSO Uploading.
    /// </summary>
    public const int QSOUploadTimeoutSec = 15;

    /// <summary>
    ///     API Endpoint for downloading adif in specified date range.
    /// </summary>
    public const string ExportCustomAdifLogs = "/adif/export_custom";
    
    /// <summary>
    ///     This is for cloudlog helper only.
    ///     See also https://clublog.freshdesk.com/support/solutions/articles/54910-api-keys
    /// </summary>
    public static readonly string Clkk = ("afce|ce|4" + "6e95|||83d99af3|9851" + "429d18dade6670278").Replace("|", "");

    /// <summary>
    ///     Available rigs of OmniRig.
    /// </summary>
    public static List<string> OmniRigAvailableRig = new() { "Rig 1", "Rig 2" };

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

    /// <summary>
    ///     Width and height of exported polar chart.
    /// </summary>
    public static int ExportedPolarChartSize = 600;

    /// <summary>
    ///     Default Hamlib files for windows, https://github.com/Hamlib/Hamlib/blob/master/scripts/build-w64.sh
    /// </summary>
    public static List<string> DefaultWindowsHamlibFiles = new()
    {
        "libgcc_s_dw2-1.dll",
        "libgcc_s_seh-1.dll",
        "libgcc_s_sjlj-1.dll",
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

    /// <summary>
    ///     Directory where storages hamlib and its deps.
    /// </summary>
    public static string HamlibFilePath = Path.Join(
        ApplicationStartUpUtil.GetConfigDir(),
        "hamlib");

    /// <summary>
    ///     Default World map file name
    /// </summary>
    public const string DefaultWorldMapFile = "NE2_50M_SR_W_proc_small.jpg";

    /// <summary>
    ///     Default WorldHeatmapWidth(lon)
    /// </summary>
    public static int WorldHeatmapWidth = 360;

    /// <summary>
    ///     Default WorldHeatmapHeight(lat)
    /// </summary>
    public static int WorldHeatmapHeight = 180;

    /// <summary>
    ///     Default temp file path. Used for LoTW Upload.
    /// </summary>
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

    public const string DefaultDxccInfoFile = "dxcc_info.dat";
    
    public const string AvaresFlagTemplate = "avares://CloudlogHelper/Assets/Flags/";

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

}