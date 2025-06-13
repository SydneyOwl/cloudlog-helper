using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Styling;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
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

    private bool _isRigctldUsingExternal;

    public MainWindowViewModel()
    {
        OpenSettingsWindow = ReactiveCommand.CreateFromTask(OpenWindow<SettingsWindowViewModel>);
        OpenAboutWindow = ReactiveCommand.CreateFromTask(OpenWindow<AboutWindowViewModel>);
        OpenQSOAssistantWindow = ReactiveCommand.CreateFromTask(OpenWindow<QsoSyncAssistantViewModel>);
        SwitchLightTheme = ReactiveCommand.Create(() => { App.Current.RequestedThemeVariant = ThemeVariant.Light; });
        SwitchDarkTheme = ReactiveCommand.Create(() => { App.Current.RequestedThemeVariant = ThemeVariant.Dark; });

        UserBasicDataGroupboxVM = new UserBasicDataGroupboxViewModel();
        RigDataGroupboxVM = new RIGDataGroupboxViewModel();
        UDPLogInfoGroupboxVm = new UDPLogInfoGroupboxViewModel();

        //subscribe exception obsflows!
        this.WhenActivated(disposables =>
        {
            MessageBus.Current.Listen<SettingsChanged>().Subscribe(res =>
            {
                switch (res.Part)
                {
                    case ChangedPart.Hamlib:
                        _updateRigctldListeningAddress();
                        break;
                    case ChangedPart.UDPServer:
                        _updateUdpServerListeningAddress();
                        break;
                }
            }).DisposeWith(disposables);

            // poll rigctld server status
            Observable.Timer(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)).Subscribe(_ =>
            {
                IsUdpServerRunning = UDPServerUtil.IsUdpServerRunning();
                IsRigctldRunning = RigctldUtil.IsRigctldClientRunning() || _isRigctldUsingExternal;
            }).DisposeWith(disposables);
        });
        _updateUdpServerListeningAddress();
        _updateRigctldListeningAddress();
    }

    [Reactive] public bool IsTopmost { get; set; }
    [Reactive] public string CurrentRigctldAddress { get; set; } = "(?)";
    [Reactive] public string CurrentUDPServerAddress { get; set; } = "(?)";
    [Reactive] public bool IsRigctldRunning { get; set; }
    [Reactive] public bool IsUdpServerRunning { get; set; }

    public Interaction<ViewModelBase, Unit> ShowNewWindow { get; } = new();
    public ReactiveCommand<Unit, Unit> OpenSettingsWindow { get; }

    public ReactiveCommand<Unit, Unit> OpenAboutWindow { get; }
    public ReactiveCommand<Unit, Unit> OpenQSOAssistantWindow { get; }
    public ReactiveCommand<Unit, Unit> SwitchLightTheme { get; }
    public ReactiveCommand<Unit, Unit> SwitchDarkTheme { get; }

    public UserBasicDataGroupboxViewModel UserBasicDataGroupboxVM { get; set; }
    public RIGDataGroupboxViewModel RigDataGroupboxVM { get; set; }
    public UDPLogInfoGroupboxViewModel UDPLogInfoGroupboxVm { get; set; }

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


    private void _updateRigctldListeningAddress()
    {
        var _settings = ApplicationSettings.GetInstance().HamlibSettings;
        var ip = DefaultConfigs.RigctldDefaultHost;
        var port = DefaultConfigs.RigctldDefaultPort;


        try
        {
            if (_settings.UseExternalRigctld)
            {
                _isRigctldUsingExternal = true;
                (ip, port) = IPAddrUtil.ParseAddress(_settings.ExternalRigctldHostAddress);
                CurrentRigctldAddress = $"({ip}:{port})";
                return;
            }

            _isRigctldUsingExternal = false;

            if (_settings.UseRigAdvanced &&
                !string.IsNullOrEmpty(_settings.OverrideCommandlineArg))
            {
                var matchIp = Regex.Match(_settings.OverrideCommandlineArg, @"-T\s+(\S+)");
                if (matchIp.Success)
                {
                    ip = matchIp.Groups[1].Value;
                    ClassLogger.Debug($"Match ip from args: {ip}");
                }
                else
                {
                    CurrentRigctldAddress = "(?)";
                    throw new Exception(TranslationHelper.GetString("failextractinfo"));
                }

                var matchPort = Regex.Match(_settings.OverrideCommandlineArg, @"-t\s+(\S+)");
                if (matchPort.Success)
                {
                    port = int.Parse(matchPort.Groups[1].Value);
                }
                else
                {
                    CurrentRigctldAddress = "(?)";
                    throw new Exception(TranslationHelper.GetString("failextractinfo"));
                }

                CurrentRigctldAddress = $"({ip}:{port})";
                return;
            }

            if (_settings is { UseRigAdvanced: true, AllowExternalControl: true })
            {
                CurrentRigctldAddress = $"(0.0.0.0:{port})";
                return;
            }

            CurrentRigctldAddress = $"({ip}:{port})";
        }
        catch (Exception a)
        {
            ClassLogger.Error(a);
            CurrentRigctldAddress = "(?)";
        }
    }

    private void _updateUdpServerListeningAddress()
    {
        var settings = ApplicationSettings.GetInstance().UDPSettings;
        var port = settings.UDPPort;
        if (string.IsNullOrEmpty(port))
        {
            CurrentUDPServerAddress = "(?)";
            return;
        }

        if (settings.EnableConnectionFromOutside)
        {
            CurrentUDPServerAddress = $"(0.0.0.0:{port})";
            return;
        }

        CurrentUDPServerAddress = $"(127.0.0.1:{port})";
    }
}