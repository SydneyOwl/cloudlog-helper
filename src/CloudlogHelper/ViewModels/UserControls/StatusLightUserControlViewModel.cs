using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using CloudlogHelper.Enums;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels.UserControls;

public class StatusLightUserControlViewModel : ViewModelBase
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly IApplicationSettingsService _applicationSettingsService;

    private bool _applingSettings;
    private IInAppNotificationService _inAppNotificationService;

    private bool _isRigctldUsingExternal;

    private readonly IRigBackendManager _rigBackendManager;
    private readonly IUdpServerService _udpServerService;

    public StatusLightUserControlViewModel()
    {
        if (!Design.IsDesignMode) throw new InvalidOperationException("This should be called from designer only.");
        StartStopUdpCommand = ReactiveCommand.Create(() => { });
        StartStopRigBackendCommand = ReactiveCommand.Create(() => { });
    }

    public StatusLightUserControlViewModel(IRigBackendManager rigBackendManager,
        IUdpServerService uSer,
        IApplicationSettingsService ss,
        IInAppNotificationService nw,
        CommandLineOptions cmd)
    {
        _applicationSettingsService = ss;
        _udpServerService = uSer;
        _rigBackendManager = rigBackendManager;
        _inAppNotificationService = nw;
        InitSkipped = cmd.AutoUdpLogUploadOnly;
        if (!InitSkipped)
        {
            Initialize();

            StartStopUdpCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                if (_applingSettings) return;
                _applingSettings = true;
                try
                {
                    UdpServerRunningStatus = StatusLightEnum.Loading;
                    if (_applicationSettingsService.TryGetDraftSettings(this, out var draft))
                    {
                        draft!.UDPSettings.EnableUDPServer = !draft!.UDPSettings.EnableUDPServer;
                        _applicationSettingsService.ApplySettings(this);
                    }

                    await Task.Delay(500);
                }
                finally
                {
                    _applingSettings = false;
                }
            });
            StartStopRigBackendCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                if (_applingSettings) return;
                _applingSettings = true;
                try
                {
                    RigBackendRunningStatus = StatusLightEnum.Loading;
                    if (_applicationSettingsService.TryGetDraftSettings(this, out var draft))
                    {
                        switch (_rigBackendManager.GetServiceType())
                        {
                            case RigBackendServiceEnum.Hamlib:
                                draft!.HamlibSettings.PollAllowed = !draft.HamlibSettings.PollAllowed;
                                break;
                            case RigBackendServiceEnum.FLRig:
                                draft!.FLRigSettings.PollAllowed = !draft.FLRigSettings.PollAllowed;
                                break;
                            case RigBackendServiceEnum.OmniRig:
                                draft!.OmniRigSettings.PollAllowed = !draft.OmniRigSettings.PollAllowed;
                                break;
                        }

                        _applicationSettingsService.ApplySettings(this);
                    }

                    await Task.Delay(1500);
                }
                finally
                {
                    _applingSettings = false;
                }
            });

            StartStopUdpCommand.ThrownExceptions.Subscribe(ex => { nw.SendErrorNotificationSync(ex.Message); });
            StartStopRigBackendCommand.ThrownExceptions.Subscribe(ex => { nw.SendErrorNotificationSync(ex.Message); });
        }
        else
        {
            StartStopUdpCommand = ReactiveCommand.Create(() => { });
            StartStopRigBackendCommand = ReactiveCommand.Create(() => { });
        }
    }

    [Reactive] public string CurrentRigBackendAddress { get; set; } = "(?)";
    [Reactive] public string CurrentUDPServerAddress { get; set; } = "(?)";
    [Reactive] public StatusLightEnum RigBackendRunningStatus { get; set; } = StatusLightEnum.Loading;
    [Reactive] public StatusLightEnum UdpServerRunningStatus { get; set; } = StatusLightEnum.Loading;
    [Reactive] public bool InitSkipped { get; set; }
    [Reactive] public string BackendService { get; set; }

    [Reactive] public ReactiveCommand<Unit, Unit>? StartStopUdpCommand { get; set; }
    [Reactive] public ReactiveCommand<Unit, Unit>? StartStopRigBackendCommand { get; set; }

    private void Initialize()
    {
        this.WhenActivated(disposables =>
        {
            MessageBus.Current.Listen<SettingsChanged>().Subscribe(res =>
            {
                switch (res.Part)
                {
                    case ChangedPart.NothingJustClosed:
                        _updateRigListeningAddress();
                        break;
                    case ChangedPart.UDPServer:
                        _updateUdpServerListeningAddress();
                        break;
                }
            }).DisposeWith(disposables);

            Observable.Timer(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)).Subscribe(_ =>
            {
                if (_applingSettings) return;

                UdpServerRunningStatus = _udpServerService.IsUdpServerRunning()
                    ? StatusLightEnum.Running
                    : StatusLightEnum.Stopped;

                RigBackendRunningStatus = _rigBackendManager.IsServiceRunning()
                                          || (_isRigctldUsingExternal && _rigBackendManager.GetServiceType() ==
                                              RigBackendServiceEnum.Hamlib)
                    ? StatusLightEnum.Running
                    : StatusLightEnum.Stopped;
            }).DisposeWith(disposables);
        });

        _updateUdpServerListeningAddress();
        _updateRigListeningAddress();
    }

    private void _updateRigListeningAddress()
    {

        var tp = _rigBackendManager.GetServiceType();
        // Console.WriteLine($"========Current: {_rigBackendManager.GetServiceType()}");

        if (tp == RigBackendServiceEnum.Hamlib)
        {
            BackendService = "Hamlib";
            var hamlibSettings = _applicationSettingsService.GetCurrentSettings().HamlibSettings;
            var ip = DefaultConfigs.RigctldDefaultHost;
            var port = DefaultConfigs.RigctldDefaultPort;

            try
            {
                if (hamlibSettings.UseExternalRigctld)
                {
                    _isRigctldUsingExternal = true;
                    (ip, port) = IPAddrUtil.ParseAddress(hamlibSettings.ExternalRigctldHostAddress);
                    CurrentRigBackendAddress = $"({ip}:{port})";
                    return;
                }

                _isRigctldUsingExternal = false;

                if (hamlibSettings.UseRigAdvanced &&
                    !string.IsNullOrEmpty(hamlibSettings.OverrideCommandlineArg))
                {
                    var matchIp = Regex.Match(hamlibSettings.OverrideCommandlineArg, @"-T\s+(\S+)");
                    if (matchIp.Success)
                    {
                        ip = matchIp.Groups[1].Value;
                        ClassLogger.Debug($"Match ip from args: {ip}");
                    }
                    else
                    {
                        CurrentRigBackendAddress = "(?)";
                        throw new Exception(TranslationHelper.GetString(LangKeys.failextractinfo));
                    }

                    var matchPort = Regex.Match(hamlibSettings.OverrideCommandlineArg, @"-t\s+(\S+)");
                    if (matchPort.Success)
                    {
                        port = int.Parse(matchPort.Groups[1].Value);
                    }
                    else
                    {
                        CurrentRigBackendAddress = "(?)";
                        throw new Exception(TranslationHelper.GetString(LangKeys.failextractinfo));
                    }

                    CurrentRigBackendAddress = $"({ip}:{port})";
                    return;
                }

                if (hamlibSettings is { UseRigAdvanced: true, AllowExternalControl: true })
                {
                    CurrentRigBackendAddress = $"(0.0.0.0:{port})";
                    return;
                }

                CurrentRigBackendAddress = $"({ip}:{port})";
            }
            catch (Exception a)
            {
                ClassLogger.Error(a);
                // _windowNotificationManagerService.SendErrorNotificationSync(a.Message);
                CurrentRigBackendAddress = "(?)";
            }
        }

        if (tp == RigBackendServiceEnum.FLRig)
        {
            BackendService = "FLRig";
            var flrigSettings = _applicationSettingsService.GetCurrentSettings().FLRigSettings;
            CurrentRigBackendAddress = $"({flrigSettings.FLRigHost}:{flrigSettings.FLRigPort})";
        }
        
        if (tp == RigBackendServiceEnum.OmniRig)
        {
            BackendService = "OmniRig";
            var omniRigSettings = _applicationSettingsService.GetCurrentSettings().OmniRigSettings;
            CurrentRigBackendAddress = $"({omniRigSettings.SelectedRig})";
        }
    }

    private void _updateUdpServerListeningAddress()
    {
        try
        {
            var settings = _applicationSettingsService.GetCurrentSettings().UDPSettings;
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
        catch (Exception a)
        {
            ClassLogger.Error(a);
            // _windowNotificationManagerService.SendErrorNotificationSync(a.Message);
        }
    }
}