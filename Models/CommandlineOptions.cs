using CommandLine;

namespace CloudlogHelper.Models;


public class CommandLineOptions
{
    [Option("verbose", HelpText = "Enable verbose logging (Trace level).")]
    public bool Verbose { get; set; }

    [Option("log2file", HelpText = "Log output to file.")]
    public bool LogToFile { get; set; }

    [Option("reinit-db", HelpText = "Force reinitialize the database.")]
    public bool ReinitDatabase { get; set; }

    [Option("dev", HelpText = "Developer mode (throw exceptions).")]
    public bool DeveloperMode { get; set; }
    
    [Option("udp-log-only", HelpText = "Enable realtime log upload only; disable and hide other modules in the main window for better performance.")]
    public bool AutoUdpLogUploadOnly { get; set; }

    [Option("crash-report", HelpText = "Path to crash report file.", Hidden = true)]
    public string? CrashReportFile { get; set; }
}