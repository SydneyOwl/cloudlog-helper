using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.ReactiveUI;
using CloudlogHelper.Models;
using CloudlogHelper.Utils;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace CloudlogHelper;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            _initializeCulture();

            var verboseLevel = args.Contains("--verbose") ? LogLevel.Trace : LogLevel.Info;
            if (args.Contains("--log2file"))
            {
                _initializeLogger(verboseLevel, true);
            }
            else
            {
                _initializeLogger(verboseLevel);
            }
            
            // To be honest, I don't know why but if this is initialized at OnFrameworkInitializationCompleted it would fail...
            _ = DatabaseUtil.InitDatabaseAsync(forceInitDatabase: args.Contains("--reinit-db"));

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            if (args.Contains("--dev")) throw;
            if (args.Contains("--crash-report")) return;
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
        if (settings.LanguageType == SupportedLanguage.NotSpecified)
            settings.LanguageType = TranslationHelper.DetectDefaultLanguage();
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