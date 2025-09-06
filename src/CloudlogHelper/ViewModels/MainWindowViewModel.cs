using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Services.Interfaces;
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

    private readonly IWindowManagerService windowManager;
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
        CommandLineOptions cmd,
        IWindowManagerService wm
    )
    {
        if (cmd.AutoUdpLogUploadOnly)
        {
            UserBasicBoxEnabled = false;
            RigDataBoxEnabled = false;
        }
        
        windowManager = wm;
        OpenSettingsWindow = ReactiveCommand.CreateFromTask(() => OpenWindow(typeof(SettingsWindowViewModel)));
        OpenAboutWindow = ReactiveCommand.CreateFromTask(() => OpenWindow(typeof(AboutWindowViewModel)));
        OpenQSOAssistantWindow =
            ReactiveCommand.CreateFromTask(() => OpenWindow(typeof(QsoSyncAssistantWindowViewModel)));
        SwitchLightTheme = ReactiveCommand.Create(() => { App.Current.RequestedThemeVariant = ThemeVariant.Light; });
        SwitchDarkTheme = ReactiveCommand.Create(() => { App.Current.RequestedThemeVariant = ThemeVariant.Dark; });

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
                                var floatWin = new FloatingWindow()
                                {
                                    DataContext = new FloatingWindowViewModel(k)
                                };
                                floatWin.Height = 600;
                                floatWin.Show();
                                var track = windowManager.Track(floatWin);
                                ((FloatableViewModelBase)res.Sender).SplitUserControlViewModel!.WindowSeq = track;
                                UDPLogBoxEnabled = false;
                                break;
                            }
                            case UserBasicDataGroupboxUserControlViewModel:
                            {
                                var k = new UserBasicDataGroupboxUserControl()
                                {
                                    DataContext = res.Sender
                                };
                                var floatWin = new FloatingWindow()
                                {
                                    DataContext = new FloatingWindowViewModel(k)
                                };
                                floatWin.Height = 260;
                                floatWin.Show();
                                var track = windowManager.Track(floatWin);
                                ((FloatableViewModelBase)res.Sender).SplitUserControlViewModel!.WindowSeq = track;
                                UserBasicBoxEnabled = false;
                                break;
                            }
                            case RIGDataGroupboxUserControlViewModel:
                            {
                                var k = new RIGDataGroupboxUserControl()
                                {
                                    DataContext = res.Sender
                                };
                                var floatWin = new FloatingWindow()
                                {
                                    DataContext = new FloatingWindowViewModel(k)
                                };
                                floatWin.Height = 250;
                                floatWin.Show();
                                var track = windowManager.Track(floatWin);
                                ((FloatableViewModelBase)res.Sender).SplitUserControlViewModel!.WindowSeq = track;
                                RigDataBoxEnabled = false;
                                break;
                            }
                        }
                    }
                    else
                    {
                        windowManager.CloseWindowBySeq(res.SenderSeq!);
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
    public ReactiveCommand<Unit, Unit> SwitchLightTheme { get; }
    public ReactiveCommand<Unit, Unit> SwitchDarkTheme { get; }

    public UserBasicDataGroupboxUserControlViewModel UserBasicDataGroupboxUserControlVm { get; set; }
    public RIGDataGroupboxUserControlViewModel RigDataGroupboxUserControlVm { get; set; }
    public UDPLogInfoGroupboxUserControlViewModel UDPLogInfoGroupboxUserControlVm { get; set; }
    public StatusLightUserControlViewModel StatusLightUserControlViewModel { get; set; }

    private async Task OpenWindow(Type vm)
    {
        try
        {
            await windowManager.CreateAndShowWindowByVm(vm);
        }
        catch (Exception ex)
        {
            ClassLogger.Error($"open failed:{vm.Name}, {ex.Message}");
            throw;
        }
    }
}