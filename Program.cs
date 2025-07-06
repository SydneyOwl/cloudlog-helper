using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.ReactiveUI;
using CloudlogHelper.Models;
using CloudlogHelper.Utils;
using CommandLine;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace CloudlogHelper;

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

    [Option("crash-report", HelpText = "Path to crash report file.", Hidden = true)]
    public string? CrashReportFile { get; set; }
}

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Parser.Default.ParseArguments<CommandLineOptions>(args)
            .WithParsed(options => RunWithOptions(options, args))
            .WithNotParsed(HandleParseErrors);
    }

    private static void RunWithOptions(CommandLineOptions options, string[] originalArgs )
    {
        try
        {
            var verboseLevel = options.Verbose ? LogLevel.Trace : LogLevel.Info;
            _initializeLogger(verboseLevel, options.LogToFile);
            _initializeCulture();

            // To be honest, I don't know why but if this is initialized at OnFrameworkInitializationCompleted it would fail...
            _ = DatabaseUtil.InitDatabaseAsync(forceInitDatabase: options.ReinitDatabase);

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(originalArgs);
        }
        catch (Exception ex)
        {
            if (options.DeveloperMode) throw;
            if (string.IsNullOrEmpty(options.CrashReportFile)) return;
            var tmp = Path.GetTempFileName();
            // Console.WriteLine(tmp);
            File.WriteAllText(tmp,
                $@"Environment: {RuntimeInformation.RuntimeIdentifier}, {RuntimeInformation.OSDescription}
Type：{ex.Message}
Stack：{ex.StackTrace}");
            var executablePath = Process.GetCurrentProcess().MainModule!.FileName;
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = $"--crash-report {tmp}",
                UseShellExecute = true
            };
            Process.Start(startInfo);
            Environment.Exit(0);
        }
        finally
        {
            App.CleanTrayIcon();
            RigctldUtil.CleanUp();
            DatabaseUtil.Cleanup();
            UDPServerUtil.TerminateUDPServer();
        }
    }
    
    private static void HandleParseErrors(IEnumerable<Error> errors)
    {
        // Handle command line parsing errors here
        // For example, you might want to display help text or exit
        foreach (var error in errors)
        {
            Console.WriteLine($@"Error while parsing args: {error}");
        }
        Environment.Exit(1);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
    }


    private static void _initializeCulture()
    {
        ApplicationSettings.ReadSettingsFromFile();
        var settings = ApplicationSettings.GetInstance();
        var draftSettings = ApplicationSettings.GetDraftInstance();
        if (settings.LanguageType == SupportedLanguage.NotSpecified)
        {
            settings.LanguageType = TranslationHelper.DetectDefaultLanguage();
            draftSettings.LanguageType = TranslationHelper.DetectDefaultLanguage();
        }
        I18NExtension.Culture = TranslationHelper.GetCultureInfo(settings.LanguageType);
    }

    private static void _initializeLogger(LogLevel logLevel, bool writeToFile = false)
    {
        var config = new LoggingConfiguration();
        var consoleTarget = new ConsoleTarget("console");
        config.AddTarget(consoleTarget);
        config.AddRule(logLevel, LogLevel.Fatal, consoleTarget);
        if (writeToFile)
        {
            var fileTarget = new FileTarget("file")
            {
                FileName = "${basedir}/logs/${shortdate}.log"
            };
            config.AddTarget(fileTarget);
            config.AddRule(logLevel, LogLevel.Fatal, fileTarget);
        }

        LogManager.Configuration = config;
    }
}