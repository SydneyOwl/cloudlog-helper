using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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
using CloudlogHelper.ViewModels.Charts;
using CloudlogHelper.Views;
using Flurl.Http;
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
    // Services that must start before main window creation.
    private static readonly Type[] StartupServices =
    {
        typeof(IPluginService)
    };

    // UI-bound singletons that should be created on the UI thread.
    private static readonly Type[] UiStartupServices =
    {
        typeof(PolarChartWindowViewModel),
        typeof(StationStatisticsChartWindowViewModel)
    };

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
        var verboseLevel = _cmdOptions.Verbose ? LogLevel.Trace : LogLevel.Info;
        _initializeLogger(verboseLevel, _cmdOptions.LogToFile);

        FlurlHttp.Clients.WithDefaults(builder =>
            builder.BeforeCall(call =>
                {
                    call.Client
                        .WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
                        .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.DefaultRequestTimeout));
                })
                .ConfigureInnerHandler(handler =>
                {
                    handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
                })
        );
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Name = "CloudlogHelper";
    }

    private async Task Workload(IClassicDesktopStyleApplicationLifetime desktop, Window splashLevel)
    {
        _preInit();
        _prepareTempDirectory();
        _servProvider = await _buildServiceProviderAsync(desktop).ConfigureAwait(false);
        var appState = await _initializeAppStateAsync(splashLevel).ConfigureAwait(false);
        if (!appState.ShouldContinue) return;

        _warmupServices(StartupServices);
        _releaseDepFiles(_cmdOptions.ReinitHamlib || appState.RequiresHamlibRefresh);
    }

    // already in ui thread
    private async Task PostExec(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _warmupServices(UiStartupServices);
        var mainWindow = _resolveRequiredService<MainWindow>();
        mainWindow.Closed +=  (_, _) => desktop.Shutdown();
        desktop.MainWindow = mainWindow;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        mainWindow.Show();
        mainWindow.Focus();

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

                if (elapsed.TotalMilliseconds < 500) mainWindow.Show();
                _lastClickTime = currentTime;
            };
        }
        catch (Exception ex)
        {
            // this may fail on Windows 7
            ClassLogger.Warn(ex, "Trayicon failed.");
        }
    }

    private static void _prepareTempDirectory()
    {
        _safeExecute(() => Directory.Delete(DefaultConfigs.DefaultTempFilePath, true));
        _safeExecute(() => Directory.CreateDirectory(DefaultConfigs.DefaultTempFilePath));
    }

    private async Task<ServiceProvider> _buildServiceProviderAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var collection = new ServiceCollection();
        collection.AddCoreServices();
        collection.AddViewModels();
        collection.AddRuntimeServices(desktop, _cmdOptions);
        await collection.AddPlatformNotificationAsync().ConfigureAwait(false);

        return collection.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    private async Task<(bool ShouldContinue, bool RequiresHamlibRefresh)> _initializeAppStateAsync(Window splashLevel)
    {
        var databaseService = _resolveRequiredService<IDatabaseService>();
        await databaseService.InitDatabaseAsync(forceInitDatabase: _cmdOptions.ReinitDatabase).ConfigureAwait(false);
        var requiresUpgrade = databaseService.IsUpgradeNeeded();

        if (requiresUpgrade)
        {
            var accepted = await _requestDisclaimerAgreementAsync(splashLevel).ConfigureAwait(false);
            if (!accepted)
            {
                ClassLogger.Info("User refused disclaimer. Abort.");
                Environment.Exit(0);
                return (false, false);
            }

            ClassLogger.Info("User accepted disclaimer.");
            await databaseService.UpgradeDatabaseAsync().ConfigureAwait(false);
        }

        _applyCultureFromSettings();
        return (true, requiresUpgrade);
    }

    private async Task<bool> _requestDisclaimerAgreementAsync(Window splashLevel)
    {
        var msgBox = _resolveRequiredService<IMessageBoxManagerService>();
        var accepted = false;

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            splashLevel.Topmost = false;
            var accept = TranslationHelper.GetString(LangKeys.accept);
            var deny = TranslationHelper.GetString(LangKeys.deny);
            var clickResult = await msgBox.DoShowCustomMessageboxDialogAsync(new MessageBoxCustomParams
                {
                    ButtonDefinitions = new[]
                    {
                        new ButtonDefinition
                        {
                            Name = accept
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
                splashLevel);

            accepted = clickResult == accept;
        });

        return accepted;
    }

    private void _applyCultureFromSettings()
    {
        var applicationSettingsService = _resolveRequiredService<IApplicationSettingsService>();
        I18NExtension.Culture =
            TranslationHelper.GetCultureInfo(applicationSettingsService.GetCurrentSettings().BasicSettings
                .LanguageType);
    }

    private void _warmupServices(Type[] servicesToWarmup)
    {
        foreach (var serviceType in servicesToWarmup)
        {
            _ = _resolveRequiredService(serviceType);
        }
    }

    private static object _resolveRequiredService(Type serviceType)
    {
        if (_servProvider is null) throw new InvalidOperationException("Service provider has not been initialized.");
        return _servProvider.GetRequiredService(serviceType);
    }

    private static T _resolveRequiredService<T>() where T : notnull
    {
        if (_servProvider is null) throw new InvalidOperationException("Service provider has not been initialized.");
        return _servProvider.GetRequiredService<T>();
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
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            ClassLogger.Error($"Unobserved Task Exception occurred! Ignoring it sliently: {e.Exception.Message} - {e.Exception.StackTrace}");   
            e.SetObserved();
        };
        
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
                    win => Workload(desktop, win),
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
        try
        {
            _safeExecute(() => _servProvider?.Dispose());
            _safeExecute(() => _mutex?.ReleaseMutex());
            _safeExecute(() => _trayIcon?.Dispose());
            _safeExecute(() => Directory.Delete(DefaultConfigs.DefaultTempFilePath, true));
        }
        catch (Exception e)
        {
            ClassLogger.Error(e, "Error occurred while doing clean up.");
        }
    }

    private static void _safeExecute(Action action)
    {
        try
        {
            action?.Invoke();
        }
        catch (Exception e)
        {
            ClassLogger.Error(e, "Error occurred while doing clean up.");
        }
    }


    private static void _initializeLogger(LogLevel logLevel, bool writeToFile = false)
    {
        var config = new LoggingConfiguration();
        var consoleTarget = new ConsoleTarget("console")
        {
            Layout = logLevel > LogLevel.Debug
                ? "${longdate} [${level:uppercase=true}] ${message} ${exception}"
                : "${longdate} [${level:uppercase=true}] ${message} ${exception} ${callsite:fileName=true:includeLineNumbers=true}"
        };
        config.AddTarget(consoleTarget);
        config.AddRule(logLevel, LogLevel.Fatal, consoleTarget);
        if (writeToFile)
        {
            var fileTarget = new FileTarget("file")
            {
                FileName = "${basedir}/logs/${shortdate}.log",
                Layout = logLevel > LogLevel.Debug
                    ? "${longdate} [${level:uppercase=true}] ${message} ${exception}"
                    : "${longdate} [${level:uppercase=true}] ${message} ${exception} ${callsite:fileName=true:includeLineNumbers=true}"
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
                ClassLogger.Warn(ex, "Error occurred while releasing dep files.");
            }

        Directory.CreateDirectory(DefaultConfigs.HamlibFilePath);
        var hamlibRelease = DefaultConfigs.DefaultWindowsHamlibFiles;
        if (OperatingSystem.IsLinux()) hamlibRelease = DefaultConfigs.DefaultLinuxHamlibFiles;
        if (OperatingSystem.IsMacOS()) hamlibRelease = DefaultConfigs.DefaultOSXHamlibFiles;
        foreach (var defaultHamlibFile in hamlibRelease)
        {
            var tPath = Path.Join(DefaultConfigs.HamlibFilePath, defaultHamlibFile);
            if (File.Exists(tPath))
            {
                ClassLogger.Info($"{tPath} exists. skipping release hamlib files...");
                continue;
            }

            ClassLogger.Info($"Releasing hamlib files to {tPath} ..");

            try
            {
                var resourceFileStream = ApplicationStartUpUtil.GetSingleResourceStream(defaultHamlibFile);
                
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
            }
            catch (Exception ex)
            {
                ClassLogger.Warn(ex, $"Failed to extract {defaultHamlibFile}, Skipping...");
                continue;
            }

            // make it executable on unix
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                try
                {
                    var fileInfo = new UnixFileInfo(tPath);
                    fileInfo.FileAccessPermissions |=
                        FileAccessPermissions.UserExecute |
                        FileAccessPermissions.GroupExecute |
                        FileAccessPermissions.OtherExecute;
                }
                catch (Exception ex)
                {
                    ClassLogger.Error(ex, "Unable to set permissions to ham binaries. ignored");
                }
            }
        }
    }
}
