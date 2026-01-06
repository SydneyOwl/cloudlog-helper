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

    private bool _applingSettings;

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
        _udpServerService = uSer;
        _rigBackendManager = rigBackendManager;
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
                    if (ss.TryGetDraftSettings(this, out var draft))
                    {
                        draft!.UDPSettings.EnableUDPServer = !draft!.UDPSettings.EnableUDPServer;
                        ss.ApplySettings(this);
                    }

                    await Task.Delay(500);
                }
                finally
                {
                    _applingSettings = false;
                }
            });
            
            // 
            StartStopRigBackendCommand = ReactiveCommand.CreateFromTask( () => Task.CompletedTask);

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
                        _updateRigInfo();
                        _updateUdpServerInfo();
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
                    ? StatusLightEnum.Running
                    : StatusLightEnum.Stopped;
            }).DisposeWith(disposables);
        });

        _updateUdpServerInfo();
        _updateRigInfo();
    }

    private void _updateRigInfo()
    {
        CurrentRigBackendAddress = _rigBackendManager.GetServiceEndpointAddress();
        BackendService = _rigBackendManager.GetServiceType().ToString();
    }

    private void _updateUdpServerInfo()
    {
        CurrentUDPServerAddress = _udpServerService.GetUdpBindingAddress();
    }
}