using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
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

    private static IRigctldService _rigctldService;
    private static IUdpServerService _udpServerService;

    private bool _isRigctldUsingExternal;

    public StatusLightUserControlViewModel()
    {
        if (!Design.IsDesignMode) throw new InvalidOperationException("This should be called from designer only.");
    }

    public StatusLightUserControlViewModel(IRigctldService rSer,
        IUdpServerService uSer,
        CommandLineOptions cmd)
    {
        _udpServerService = uSer;
        _rigctldService = rSer;
        InitSkipped = cmd.AutoUdpLogUploadOnly;
        if (!InitSkipped) Initialize();
    }

    [Reactive] public string CurrentRigctldAddress { get; set; } = "(?)";
    [Reactive] public string CurrentUDPServerAddress { get; set; } = "(?)";
    [Reactive] public bool IsRigctldRunning { get; set; }
    [Reactive] public bool IsUdpServerRunning { get; set; }
    [Reactive] public bool InitSkipped { get; set; }

    private void Initialize()
    {
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

            Observable.Timer(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)).Subscribe(_ =>
            {
                IsUdpServerRunning = _udpServerService.IsUdpServerRunning();
                IsRigctldRunning = _rigctldService.IsRigctldClientRunning() || _isRigctldUsingExternal;
            }).DisposeWith(disposables);
        });

        _updateUdpServerListeningAddress();
        _updateRigctldListeningAddress();
    }

    private void _updateRigctldListeningAddress()
    {
        var settings = ApplicationSettings.GetInstance().HamlibSettings;
        var ip = DefaultConfigs.RigctldDefaultHost;
        var port = DefaultConfigs.RigctldDefaultPort;


        try
        {
            if (settings.UseExternalRigctld)
            {
                _isRigctldUsingExternal = true;
                (ip, port) = IPAddrUtil.ParseAddress(settings.ExternalRigctldHostAddress);
                CurrentRigctldAddress = $"({ip}:{port})";
                return;
            }

            _isRigctldUsingExternal = false;

            if (settings.UseRigAdvanced &&
                !string.IsNullOrEmpty(settings.OverrideCommandlineArg))
            {
                var matchIp = Regex.Match(settings.OverrideCommandlineArg, @"-T\s+(\S+)");
                if (matchIp.Success)
                {
                    ip = matchIp.Groups[1].Value;
                    ClassLogger.Debug($"Match ip from args: {ip}");
                }
                else
                {
                    CurrentRigctldAddress = "(?)";
                    throw new Exception(TranslationHelper.GetString(LangKeys.failextractinfo));
                }

                var matchPort = Regex.Match(settings.OverrideCommandlineArg, @"-t\s+(\S+)");
                if (matchPort.Success)
                {
                    port = int.Parse(matchPort.Groups[1].Value);
                }
                else
                {
                    CurrentRigctldAddress = "(?)";
                    throw new Exception(TranslationHelper.GetString(LangKeys.failextractinfo));
                }

                CurrentRigctldAddress = $"({ip}:{port})";
                return;
            }

            if (settings is { UseRigAdvanced: true, AllowExternalControl: true })
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