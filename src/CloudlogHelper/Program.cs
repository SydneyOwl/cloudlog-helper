using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.ReactiveUI;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using CommandLine;

namespace CloudlogHelper;

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

    private static void RunWithOptions(CommandLineOptions options, string[] originalArgs)
    {
        try
        {
            BuildAvaloniaApp(options)
                .StartWithClassicDesktopLifetime(originalArgs);
        }
        catch (Exception ex)
        {
            if (options.DeveloperMode) throw;
            if (!string.IsNullOrEmpty(options.CrashReportFile)) return;
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp,
                $@"App version: {VersionInfo.Version} @ {VersionInfo.Commit}
Environment: {RuntimeInformation.RuntimeIdentifier}, {RuntimeInformation.OSDescription}, {VersionInfo.BuildType}
Type：{ex.Message}
Stack：{ex.StackTrace}

Inner Expection(if any): {ex.InnerException?.Message}
Inner Exception Stack(if any): {ex.InnerException?.StackTrace}");
            ApplicationStartUpUtil.RestartApplicationWithArgs($"--crash-report {tmp}");
        }
        finally
        {
            App.CleanUp();
        }
    }

    private static void HandleParseErrors(IEnumerable<Error> errors)
    {
        // Handle command line parsing errors here
        // For example, you might want to display help text or exit
        foreach (var error in errors) Console.WriteLine($@"Error while parsing args: {error}");
        Environment.Exit(1);
    }

    // Avalonia configuration
    public static AppBuilder BuildAvaloniaApp(CommandLineOptions? options)
    {
        return AppBuilder.Configure(() => new App(options))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
    }

    // Used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
    }
}