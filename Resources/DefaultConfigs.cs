using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
    public static readonly string EmbeddedeAdifModeFilename= "CloudlogHelper.Resources.adif_modes.dat";

    /// <summary>
    ///     Default timeout (in seconds) for HTTP(s) requests.
    /// </summary>
    public static readonly int DefaultRequestTimeout = 5;

    /// <summary>
    ///     Timeout (in seconds) for polling Cloudlog API (CloudlogKeyStationStatisticsAPIEndpoint).
    /// </summary>
    public static readonly int CLoudlogInfoPollRequestTimeout = 30;

    /// <summary>
    ///     Duration (in minutes) for which QSO data is collected to calculate average QSO metrics.
    /// </summary>
    public static int InitialQsoCalcWindowDuration = 5;
    
    /// <summary>
    ///     Duration (in minutes) for which QSO data is collected to calculate average QSO metrics.
    /// </summary>
    public static string DefaultHTTPUserAgent = $"CloudlogHelper/{VersionInfo.Version}";

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
    ///     Socket timeout (in milliseconds) for Rigctld communication.
    /// </summary>
    public static readonly int RigctldSocketTimeout = 2500;

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
    public static readonly int MaxRigctldErrorCount  = 3;

    /// <summary>
    ///     Expiry time (in seconds) for UDP client connections.
    /// </summary>
    public static readonly int UDPClientExpiryInSeconds = 60;

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