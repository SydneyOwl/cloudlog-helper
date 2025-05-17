using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Styling;
using CloudlogHelper.Models;
using CloudlogHelper.Utils;
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
    
    [Reactive] public bool IsTopmost { get; set; }

    public MainWindowViewModel()
    {
        OpenSettingsWindow = ReactiveCommand.CreateFromTask(OpenWindow<SettingsWindowViewModel>);
        OpenAboutWindow = ReactiveCommand.CreateFromTask(OpenWindow<AboutWindowViewModel>);
        SwitchLightTheme = ReactiveCommand.Create(() => { App.Current.RequestedThemeVariant = ThemeVariant.Light; });
        SwitchDarkTheme = ReactiveCommand.Create(() => { App.Current.RequestedThemeVariant = ThemeVariant.Dark; });
        var conflict = RigctldUtil.GetPossibleConflictProcess();
        
        // don't check conflict processes if UseExternalRigctld enabled.
        var tmpSettings = ApplicationSettings.GetInstance();
        if (!tmpSettings.HamlibSettings.UseExternalRigctld
            && tmpSettings.HamlibSettings is not { UseRigAdvanced: true, AllowProxyRequests: true }
            && !string.IsNullOrEmpty(conflict))
            RigDataErrorPanelVM.ErrorMessage =
                TranslationHelper.GetString("conflicthamlib").Replace("{replace01}", conflict);

        UserBasicDataGroupboxVM = new UserBasicDataGroupboxViewModel();
        RigDataGroupboxVM = new RIGDataGroupboxViewModel();
        UDPLogInfoGroupboxVm = new UDPLogInfoGroupboxViewModel();

        //subscribe exception obsflows!
        this.WhenActivated(disposables =>
        {
            UserBasicDataGroupboxVM.MessageStream.Subscribe(errstr => { CloudlogErrorPanelVM.ErrorMessage = errstr; })
                .DisposeWith(disposables);

            RigDataGroupboxVM.MessageStream.Subscribe(errstr => { RigDataErrorPanelVM.ErrorMessage = errstr; })
                .DisposeWith(disposables);

            UDPLogInfoGroupboxVm.MessageStream.Subscribe(errstr => { UDPLogErrorPanelVM.ErrorMessage = errstr; })
                .DisposeWith(disposables);
        });
    }

    public Interaction<ViewModelBase, Unit> ShowNewWindow { get; } = new();
    public ReactiveCommand<Unit, Unit> OpenSettingsWindow { get; }

    public ReactiveCommand<Unit, Unit> OpenAboutWindow { get; }
    public ReactiveCommand<Unit, Unit> SwitchLightTheme { get; }
    public ReactiveCommand<Unit, Unit> SwitchDarkTheme { get; }
    
    public UserBasicDataGroupboxViewModel UserBasicDataGroupboxVM { get; set; }
    public RIGDataGroupboxViewModel RigDataGroupboxVM { get; set; }
    public UDPLogInfoGroupboxViewModel UDPLogInfoGroupboxVm { get; set; }

    public ClosableErrorPanelViewModel CloudlogErrorPanelVM { get; set; } = new();
    public ClosableErrorPanelViewModel RigDataErrorPanelVM { get; set; } = new();
    public ClosableErrorPanelViewModel UDPLogErrorPanelVM { get; set; } = new();

    private async Task OpenWindow<T>() where T : ViewModelBase, new()
    {
        try
        {
            await ShowNewWindow.Handle(new T());
        }
        catch (Exception ex)
        {
            ClassLogger.Error($"open failed:{typeof(T).Name}, {ex.Message}");
            throw;
        }
    }
}