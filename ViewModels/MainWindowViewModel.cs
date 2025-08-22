using System;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Styling;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.ViewModels.UserControls;
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
        IWindowManagerService wm
    )
    {
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
    }

    [Reactive] public bool IsTopmost { get; set; }
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
            await windowManager.CreateOrShowWindowByVm(vm);
        }
        catch (Exception ex)
        {
            ClassLogger.Error($"open failed:{vm.Name}, {ex.Message}");
            throw;
        }
    }
}