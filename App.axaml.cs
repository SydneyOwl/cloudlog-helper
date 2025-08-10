using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CloudlogHelper.LogService;
using CloudlogHelper.LogService.Attributes;
using CloudlogHelper.Models;
using CloudlogHelper.Utils;
using CloudlogHelper.ViewModels;
using NLog;
using NLog.Config;
using NLog.Targets;
using ReactiveUI;
using ErrorReportWindow = CloudlogHelper.Views.ErrorReportWindow;
using MainWindow = CloudlogHelper.Views.MainWindow;

namespace CloudlogHelper;

public class App : Application
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private static TrayIcon? _trayIcon;
    private static ReactiveCommand<Unit, Unit>? _exitCommand;
    private static ReactiveCommand<Unit, Unit>? _openCommand;
    public static WindowTracker WindowTracker { get; } = new();
    public static WindowNotification NotificationManager { get; private set; }
    public static MessageBoxHelper MessageBoxHelper { get; private set; }
    public static CommandLineOptions CmdOptions { get; private set; }

    public App(CommandLineOptions? options)
    {
        options ??= new CommandLineOptions();
        CmdOptions = options;
    }
    
    public App(){}
    
    public override void Initialize()
    {
        var verboseLevel = CmdOptions.Verbose ? LogLevel.Trace : LogLevel.Info;
        _initializeLogger(verboseLevel, CmdOptions.LogToFile);
        
        // now search for all assemblies marked as "log service"
        var lType = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(LogServiceAttribute), false).Length > 0)
            .ToList();

        if (lType.GroupBy(n => n).Any(c => c.Count() > 1))
        {
            throw new Exception("Dupe log service found. This is not allowed!");
        }
        
        // create those types and assign back to settings...
        var logServices = lType.Select(x =>
        {
            if (!typeof(ThirdPartyLogService).IsAssignableFrom(x)) throw new TypeLoadException($"Log service must be assignable to {nameof(ThirdPartyLogService)}");
            return (ThirdPartyLogService)Activator.CreateInstance(x)!;
        });
        
        _initializeSettings(logServices);
        _ = DatabaseUtil.InitDatabaseAsync(forceInitDatabase: CmdOptions.ReinitDatabase);
        
        AvaloniaXamlLoader.Load(this);
        Name = "CloudlogHelper";
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            
            if (!string.IsNullOrEmpty(CmdOptions.CrashReportFile))
            {
                desktop.MainWindow = new ErrorReportWindow(CmdOptions.CrashReportFile)
                    { ViewModel = new ErrorReportWindowViewModel() };
                return;
            }

            var mainWindow = new MainWindow
            {
                ViewModel = new MainWindowViewModel()
            };
            desktop.MainWindow = mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            NotificationManager = new WindowNotification(mainWindow);
            MessageBoxHelper = new MessageBoxHelper(mainWindow);

            _exitCommand = ReactiveCommand.Create(() =>
            {
                mainWindow.CloseDirectly();
                desktop.Shutdown();
            });
            _openCommand = ReactiveCommand.Create(() => mainWindow.Show());

            // create trayicon
            try
            {
                var nmiExit = new NativeMenuItem
                {
                    Header = TranslationHelper.GetString("exit"),
                    Command = _exitCommand
                };
                var nmiOpen = new NativeMenuItem
                {
                    Header = TranslationHelper.GetString("open"),
                    Command = _openCommand
                };

                using var stream = AssetLoader.Open(new Uri("avares://CloudlogHelper/Assets/icon.png"));
                var bitmap = new Bitmap(stream);

                _trayIcon = new TrayIcon
                {
                    ToolTipText = "CloudlogHelper",
                    Icon = new WindowIcon(bitmap),
                    Menu = new NativeMenu
                    {
                        nmiExit,
                        nmiOpen
                    }
                };
            }
            catch (Exception ex)
            {
                // this may fail on Windows 7
                ClassLogger.Warn(ex);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }


    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove) BindingPlugins.DataValidators.Remove(plugin);
    }

    public static void CleanTrayIcon()
    {
        _trayIcon?.Dispose();
    }
    
            
    private static void _initializeSettings(IEnumerable<ThirdPartyLogService> logServices)
    {
        ApplicationSettings.ReadSettingsFromFile(logServices.ToArray());
        var settings = ApplicationSettings.GetInstance();
        var draftSettings = ApplicationSettings.GetDraftInstance();
        
        // init culture
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