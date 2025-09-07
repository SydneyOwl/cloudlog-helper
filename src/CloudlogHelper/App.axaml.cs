using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CloudlogHelper.Exceptions;
using CloudlogHelper.LogService;
using CloudlogHelper.LogService.Attributes;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using CloudlogHelper.ViewModels;
using CloudlogHelper.ViewModels.UserControls;
using CloudlogHelper.Views;
using CloudlogHelper.Views.UserControls;
using Microsoft.Extensions.DependencyInjection;
using Mono.Unix;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;
using NLog;
using NLog.Config;
using NLog.Targets;
using ReactiveUI;
using ErrorReportWindow = CloudlogHelper.Views.ErrorReportWindow;
using MainWindow = CloudlogHelper.Views.MainWindow;

namespace CloudlogHelper;

public class App : Application
{
    private static Mutex? _mutex;

    private static ServiceProvider? _servProvider;
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private static TrayIcon? _trayIcon;
    private static ReactiveCommand<Unit, Unit>? _exitCommand;
    private static ReactiveCommand<Unit, Unit>? _openCommand;
    
    private DateTime _lastClickTime = DateTime.MinValue;

    public App(CommandLineOptions? options)
    {
        options ??= new CommandLineOptions();
        _cmdOptions = options;
    }

    public App()
    {
        _cmdOptions ??= new CommandLineOptions();
    }

    private static CommandLineOptions _cmdOptions { get; set; }

    private void _preInit()
    {
        if (_cmdOptions.Verbose) DefaultConfigs.MaxRigctldErrorCount = 100;
        var verboseLevel = _cmdOptions.Verbose ? LogLevel.Trace : LogLevel.Info;
        _initializeLogger(verboseLevel, _cmdOptions.LogToFile);
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Name = "CloudlogHelper";
    }

    private async Task Workload(IClassicDesktopStyleApplicationLifetime desktop, Window splashLevel)
    {
        _preInit();
        var collection = new ServiceCollection();
        await collection.AddCommonServicesAsync();
        await collection.AddViewModelsAsync();
        await collection.AddExtraAsync();
        
        // now search for all assemblies marked as "log service"
        var lType = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(LogServiceAttribute), false).Length > 0)
            .ToList();

        if (lType.GroupBy(n => n).Any(c => c.Count() > 1))
            throw new InvalidOperationException("Dupe log service found. This is not allowed!");

        // create those types and assign back to settings...
        var logServices = lType.Select(x =>
        {
            if (!typeof(ThirdPartyLogService).IsAssignableFrom(x))
                throw new TypeLoadException($"Log service must be assignable to {nameof(ThirdPartyLogService)}");
            return (ThirdPartyLogService)Activator.CreateInstance(x)!;
        });
        collection.AddSingleton<IApplicationSettingsService, ApplicationSettingsService>(pr =>
            ApplicationSettingsService.GenerateApplicationSettingsService(
                logServices.ToArray(), _cmdOptions.ReinitSettings, pr.GetRequiredService<IMapper>()
                ));
        
        collection.AddSingleton<CommandLineOptions>(p => _cmdOptions);
        collection.AddSingleton<IWindowManagerService, WindowManagerService>(prov =>
            new WindowManagerService(prov, desktop));
        collection.AddSingleton<IInAppNotificationService, InAppNotificationService>(_ =>
            new InAppNotificationService(desktop));
        collection.AddSingleton<IMessageBoxManagerService, MessageBoxManagerService>(_ =>
            new MessageBoxManagerService(desktop));
        collection.AddSingleton<IClipboardService, ClipboardService>(_ =>
            new ClipboardService(desktop));
        
        _servProvider = collection.BuildServiceProvider();
        
        var applicationSettingsService = _servProvider.GetRequiredService<IApplicationSettingsService>();
        I18NExtension.Culture = TranslationHelper.GetCultureInfo(applicationSettingsService.GetCurrentSettings().LanguageType);

        var dbSer = _servProvider.GetRequiredService<IDatabaseService>();
        await dbSer.InitDatabaseAsync(forceInitDatabase: _cmdOptions.ReinitDatabase);
        if (dbSer.IsUpgradeNeeded())
        {
            var msgBox = _servProvider.GetRequiredService<IMessageBoxManagerService>();
            var res = false;
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                splashLevel.Topmost = false;
                var accept = TranslationHelper.GetString(LangKeys.accept);
                var deny = TranslationHelper.GetString(LangKeys.deny);
                res = await msgBox.DoShowCustomMessageboxDialogAsync(new MessageBoxCustomParams
                    {
                        ButtonDefinitions = new[]
                        {
                            new ButtonDefinition
                            {
                                Name = accept,
                            },
                            new ButtonDefinition
                            {
                                Name = deny
                            }
                        },
                        ContentTitle = "User Agreement",
                        ContentMessage = TranslationHelper.GetString(LangKeys.disclaimer)
                            .Replace("{1}", VersionInfo.Version),
                        Icon = Icon.Info,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        Width = 500,
                        Height = 350,
                        SizeToContent = SizeToContent.Manual,
                        CanResize = false
                    },
                    splashLevel) != accept;
            });
            if (res)
            {
                ClassLogger.Info("User refused disclaimer. Abort.");
                Environment.Exit(0);
                return;
            }
            ClassLogger.Info("User accepted disclaimer.");
            await dbSer.UpgradeDatabaseAsync();
        }
        _releaseDepFiles(_cmdOptions.ReinitHamlib || dbSer.IsUpgradeNeeded());
    }

    private async Task PostExec(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_servProvider is null) throw new ArgumentNullException(nameof(desktop));
        
        var mainWindow = _servProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
        mainWindow.Focus();
        desktop.MainWindow = mainWindow;
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

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
                Header = TranslationHelper.GetString(LangKeys.exit),
                Command = _exitCommand
            };
            var nmiOpen = new NativeMenuItem
            {
                Header = TranslationHelper.GetString(LangKeys.open),
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
            _trayIcon.Clicked += (sender, args) =>
            {
                var currentTime = DateTime.Now;
                var elapsed = currentTime - _lastClickTime;
                
                if (elapsed.TotalMilliseconds < 500)
                {
                    mainWindow.Show();
                }
                _lastClickTime = currentTime;
            };
        }
        catch (Exception ex)
        {
            // this may fail on Windows 7
            ClassLogger.Warn(ex);
        }
    }

    private Task PreExec(IClassicDesktopStyleApplicationLifetime desktop)
    {
        Directory.CreateDirectory(ApplicationStartUpUtil.GetConfigDir());
        _mutex = new Mutex(true, DefaultConfigs.MutexId, out var createdNew);
        // check if init is allowed?
        if (!createdNew) throw new DuplicateProcessException(TranslationHelper.GetString(LangKeys.dupeinstance));

        return Task.CompletedTask;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            if (!string.IsNullOrEmpty(_cmdOptions.CrashReportFile))
                desktop.MainWindow = new ErrorReportWindow(_cmdOptions.CrashReportFile)
                    { ViewModel = new ErrorReportWindowViewModel() };
            else
                desktop.MainWindow = new SplashWindow(() => PreExec(desktop),
                    (win) => Workload(desktop, win),
                    () => PostExec(desktop));
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

    public static void CleanUp()
    {
        _servProvider?.Dispose();
        _mutex?.ReleaseMutex();
        _trayIcon?.Dispose();
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

    private static void _releaseDepFiles(bool reinit = false)
    {
        if (reinit)
            try
            {
                Directory.Delete(DefaultConfigs.HamlibFilePath, true);
            }
            catch (Exception ex)
            {
                ClassLogger.Warn(ex);
            }

        Directory.CreateDirectory(DefaultConfigs.HamlibFilePath);
        var hamlibRelease = DefaultConfigs.DefaultWindowsHamlibFiles;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) hamlibRelease = DefaultConfigs.DefaultLinuxHamlibFiles;
        foreach (var defaultHamlibFile in hamlibRelease)
        {
            var tPath = Path.Join(DefaultConfigs.HamlibFilePath, defaultHamlibFile);
            if (File.Exists(tPath))
            {
                ClassLogger.Debug($"{tPath} exists. skipping...");
                continue;
            }

            ClassLogger.Debug($"releasing {tPath} ..");
            var resourceFileStream = ApplicationStartUpUtil.GetResourceStream(defaultHamlibFile);
            if (resourceFileStream is null)
            {
                ClassLogger.Warn($"Stream is empty: {defaultHamlibFile}, Skipping...");
                continue;
            }

            using var fileStream = new FileStream(tPath, FileMode.Create, FileAccess.Write);
            resourceFileStream.Seek(0, SeekOrigin.Begin);
            resourceFileStream.CopyTo(fileStream);
            fileStream.Flush();
            fileStream.Close();

            // make it executable on linux
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var fileInfo = new UnixFileInfo(tPath);
                fileInfo.FileAccessPermissions |=
                    FileAccessPermissions.UserExecute |
                    FileAccessPermissions.GroupExecute |
                    FileAccessPermissions.OtherExecute;
            }
        }
    }
}