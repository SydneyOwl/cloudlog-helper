using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Services;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.ViewModels.Charts;
using CloudlogHelper.ViewModels.UserControls;
using CloudlogHelper.Views;
using CloudlogHelper.Views.UserControls;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly IInAppNotificationService _inAppNotificationService;

    private readonly IWindowManagerService _windowManager;
    private bool _isRigctldUsingExternal;

    public MainWindowViewModel()
    {
        if (!Design.IsDesignMode) throw new InvalidOperationException("This should be called from designer only.");
        UserBasicDataGroupboxUserControlVm = new UserBasicDataGroupboxUserControlViewModel();
        RigDataGroupboxUserControlVm = new RIGDataGroupboxUserControlViewModel();
        UDPLogInfoGroupboxUserControlVm = new UDPLogInfoGroupboxUserControlViewModel();
        StatusLightUserControlViewModel = new StatusLightUserControlViewModel();
    }

    public MainWindowViewModel(
        UDPLogInfoGroupboxUserControlViewModel udpLogInfoGroupboxUserControlViewModel,
        RIGDataGroupboxUserControlViewModel rigdataGroupboxUserControlViewModel,
        UserBasicDataGroupboxUserControlViewModel userBasicDataGroupboxUserControlViewModel,
        StatusLightUserControlViewModel statusLightUserControlViewModel,
        PolarChartWindowViewModel ignored1, // force init - DO NOT REMOVE IT!
        StationStatisticsChartWindowViewModel ignored2, // force init - DO NOT REMOVE IT!
        IPluginService _, // force init - DO NOT REMOVE IT!
        CommandLineOptions cmd,
        IWindowManagerService wm,
        IInAppNotificationService inAppNotificationService
    )
    {
        if (cmd.AutoUdpLogUploadOnly)
        {
            UserBasicBoxEnabled = false;
            RigDataBoxEnabled = false;
        }
        
        _inAppNotificationService = inAppNotificationService;
        _windowManager = wm;
        OpenSettingsWindow = ReactiveCommand.CreateFromTask(() => OpenWindow(typeof(SettingsWindowViewModel), true));
        OpenAboutWindow = ReactiveCommand.CreateFromTask(() => OpenWindow(typeof(AboutWindowViewModel), true));
        OpenQSOAssistantWindow =
            ReactiveCommand.CreateFromTask(() => OpenWindow(typeof(QsoSyncAssistantWindowViewModel), true));
        OpenSignalPolarChartWindow =
            ReactiveCommand.CreateFromTask(() => OpenWindow(typeof(PolarChartWindowViewModel), false));
        OpenStationStatisticChartWindow =
            ReactiveCommand.CreateFromTask(() => OpenWindow(typeof(StationStatisticsChartWindowViewModel), false));
        SwitchLightTheme = ReactiveCommand.Create(() =>
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        });
        SwitchDarkTheme = ReactiveCommand.Create(() =>
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        });

        UserBasicDataGroupboxUserControlVm = userBasicDataGroupboxUserControlViewModel;
        RigDataGroupboxUserControlVm = rigdataGroupboxUserControlViewModel;
        UDPLogInfoGroupboxUserControlVm = udpLogInfoGroupboxUserControlViewModel;
        StatusLightUserControlViewModel = statusLightUserControlViewModel;

        this.WhenActivated(disposable =>
        {
            MessageBus.Current.Listen<WindowSplitChanged>().Subscribe(res =>
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    if (res.IsSplit)
                    {
                        switch (res.Sender)
                        {
                            case UDPLogInfoGroupboxUserControlViewModel:
                            {
                                var k = new UDPLogInfoGroupboxUserControl
                                {
                                    DataContext = res.Sender
                                };
                                k.Height = double.NaN;
                                var floatWin = new FloatingWindow
                                {
                                    DataContext = new FloatingWindowViewModel(k)
                                };
                                floatWin.SizeToContent = SizeToContent.Width;
                                floatWin.Height = 600;
                                floatWin.Show();
                                var track = _windowManager.Track(floatWin);
                                ((FloatableViewModelBase)res.Sender).SplitUserControlViewModel!.WindowSeq = track;
                                UDPLogBoxEnabled = false;
                                break;
                            }
                            case UserBasicDataGroupboxUserControlViewModel:
                            {
                                var k = new UserBasicDataGroupboxUserControl
                                {
                                    DataContext = res.Sender
                                };
                                var floatWin = new FloatingWindow
                                {
                                    DataContext = new FloatingWindowViewModel(k)
                                };
                                floatWin.Show();
                                var track = _windowManager.Track(floatWin);
                                ((FloatableViewModelBase)res.Sender).SplitUserControlViewModel!.WindowSeq = track;
                                UserBasicBoxEnabled = false;
                                break;
                            }
                            case RIGDataGroupboxUserControlViewModel:
                            {
                                var k = new RIGDataGroupboxUserControl
                                {
                                    DataContext = res.Sender
                                };
                                var floatWin = new FloatingWindow
                                {
                                    DataContext = new FloatingWindowViewModel(k)
                                };
                                floatWin.Show();
                                var track = _windowManager.Track(floatWin);
                                ((FloatableViewModelBase)res.Sender).SplitUserControlViewModel!.WindowSeq = track;
                                RigDataBoxEnabled = false;
                                break;
                            }
                        }
                    }
                    else
                    {
                        _windowManager.CloseWindowBySeq(res.SenderSeq!);
                        switch (res.Sender)
                        {
                            case UDPLogInfoGroupboxUserControlViewModel:
                                UDPLogBoxEnabled = true;
                                break;
                            case RIGDataGroupboxUserControlViewModel:
                                RigDataBoxEnabled = true;
                                break;
                            case UserBasicDataGroupboxUserControlViewModel:
                                UserBasicBoxEnabled = true;
                                break;
                        }
                    }
                });
            }).DisposeWith(disposable);
        });
    }

    [Reactive] public bool IsTopmost { get; set; }
    [Reactive] public bool UserBasicBoxEnabled { get; set; } = true;
    [Reactive] public bool RigDataBoxEnabled { get; set; } = true;
    [Reactive] public bool UDPLogBoxEnabled { get; set; } = true;
    public ReactiveCommand<Unit, Unit> OpenSettingsWindow { get; }

    public ReactiveCommand<Unit, Unit> OpenAboutWindow { get; }
    public ReactiveCommand<Unit, Unit> OpenQSOAssistantWindow { get; }
    public ReactiveCommand<Unit, Unit> OpenSignalPolarChartWindow { get; }
    public ReactiveCommand<Unit, Unit> OpenStationStatisticChartWindow { get; }
    public ReactiveCommand<Unit, Unit> SwitchLightTheme { get; }
    public ReactiveCommand<Unit, Unit> SwitchDarkTheme { get; }

    public UserBasicDataGroupboxUserControlViewModel UserBasicDataGroupboxUserControlVm { get; set; }
    public RIGDataGroupboxUserControlViewModel RigDataGroupboxUserControlVm { get; set; }
    public UDPLogInfoGroupboxUserControlViewModel UDPLogInfoGroupboxUserControlVm { get; set; }
    public StatusLightUserControlViewModel StatusLightUserControlViewModel { get; set; }

    private async Task OpenWindow(Type vm, bool dialog)
    {
        try
        {
            await _windowManager.CreateAndShowWindowByVm(vm, null, dialog);
        }
        catch (Exception ex)
        {
            await _inAppNotificationService.SendErrorNotificationAsync($"Unable to open window: {ex.Message}");
            ClassLogger.Error(ex, $"open failed:{vm.Name}");
            throw;
        }
    }
}