using System;
using System.Linq;
using System.Reactive;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CloudlogHelper.Utils;
using CloudlogHelper.ViewModels;
using NLog;
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

    public override void Initialize()
    {
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
            if (desktop.Args != null)
                for (var i = 0; i < desktop.Args.Length; i++)
                    if (desktop.Args[i] == "--crash-report")
                    {
                        desktop.MainWindow = new ErrorReportWindow(desktop.Args[i + 1])
                            { ViewModel = new ErrorReportWindowViewModel() };
                        return;
                    }

            var mainWindow = new MainWindow
            {
                ViewModel = new MainWindowViewModel()
            };
            desktop.MainWindow = mainWindow;
            WindowNotification.SetTopLevel(mainWindow);

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
                    // 可以添加菜单项
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
}