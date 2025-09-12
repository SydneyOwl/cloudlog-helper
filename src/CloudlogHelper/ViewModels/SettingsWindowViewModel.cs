using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using CloudlogHelper.Enums;
using CloudlogHelper.LogService;
using CloudlogHelper.LogService.Attributes;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using CloudlogHelper.ViewModels.UserControls;
using DesktopNotifications;
using DynamicData;
using Flurl.Http;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Notification = DesktopNotifications.Notification;

namespace CloudlogHelper.ViewModels;

public class SettingsWindowViewModel : ViewModelBase
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private readonly CancellationTokenSource _source;
    private readonly bool _initSkipped;
    private readonly IRigctldService _rigctldService;
    private readonly IApplicationSettingsService _settingsService;
    private readonly INotificationManager _nativeNotificationManager;

    public SettingsWindowViewModel()
    {
        if (!Design.IsDesignMode) throw new InvalidOperationException("This should be called from designer only.");
        DraftSettings = new ApplicationSettings();
        DiscardConf = ReactiveCommand.Create(() => { });
        SaveAndApplyConf = ReactiveCommand.Create(() => { });
        HamlibInitPassed = true;
        // InitializeLogSystems();
    }


    public SettingsWindowViewModel(CommandLineOptions cmd,
        IApplicationSettingsService ss,
        IRigctldService rs,
        INotificationManager nm)
    {
        _settingsService = ss;
        _rigctldService = rs;
        _nativeNotificationManager = nm;
        _initSkipped = cmd.AutoUdpLogUploadOnly;
        if (!_settingsService.TryGetDraftSettings(this, out var settings))
        {
            throw new Exception("Draft setting instance is held by another viewmodel!");
        }
        DraftSettings = settings!;
        _source = new CancellationTokenSource();
        InitializeLogSystems();
        // throw new Exception();
        ShowCloudlogStationIdCombobox = DraftSettings.CloudlogSettings.AvailableCloudlogStationInfo.Count > 0;

        var hamlibCmd = ReactiveCommand.CreateFromTask(_testHamlib, DraftSettings.HamlibSettings.IsHamlibValid);
        HamlibTestButtonUserControl.SetTestButtonCommand(hamlibCmd);

        RefreshPort = ReactiveCommand.CreateFromTask(_refreshPort);

        var cloudCmd =
            ReactiveCommand.CreateFromTask(_testCloudlogConnection, DraftSettings.CloudlogSettings.IsCloudlogValid);
        CloudlogTestButtonUserControl.SetTestButtonCommand(cloudCmd);

        // save or discard conf
        DiscardConf = ReactiveCommand.Create(_discardConf);
        SaveAndApplyConf = ReactiveCommand.Create(_saveAndApplyConf);

        this.WhenActivated(disposables =>
        {
            // Subscribe language change
            this.WhenAnyValue(x => x.DraftSettings.BasicSettings.LanguageType).Subscribe(language =>
                {
                    I18NExtension.Culture = TranslationHelper.GetCultureInfo(language);
                })
                .DisposeWith(disposables);
            hamlibCmd.ThrownExceptions.Subscribe(err => Notification?.SendErrorNotificationSync(err.Message))
                .DisposeWith(disposables);
            cloudCmd.ThrownExceptions.Subscribe(err => Notification?.SendErrorNotificationSync(err.Message))
                .DisposeWith(disposables);

            RefreshPort.ThrownExceptions.Subscribe(err =>
            {
                // SerialUtil.PreSelectSerialByName is likely to fail on win8 and win7!
                ClassLogger.Debug($"RefreshHamlibData error: {err.Message}; Ignored.");
            }).DisposeWith(disposables);

            // refresh hamlib lib after fully inited
            Observable.Return(Unit.Default)
                .InvokeCommand(RefreshPort)
                .DisposeWith(disposables);
        });

        _ = _initializeHamlibAsync();
        MessageBus.Current.SendMessage(new SettingsChanged
        {
            Part = ChangedPart.NothingJustOpened
        });
    }

    public ObservableCollection<LogSystemConfig> LogSystems { get; } = new();


    public ReactiveCommand<Unit, Unit> SaveAndApplyConf { get; }
    public ReactiveCommand<Unit, Unit> DiscardConf { get; }

    public IInAppNotificationService Notification { get; set; }
    public ApplicationSettings DraftSettings { get; set; }

    private void InitializeLogSystems()
    {
        foreach (var draftSettingsLogService in DraftSettings.LogServices)
        {
            var classAttr = draftSettingsLogService.GetType().GetCustomAttribute<LogServiceAttribute>();
            if (classAttr == null) throw new Exception("Failed to find class attr!");

            var properties = draftSettingsLogService.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.IsDefined(typeof(UserInputAttribute), false));

            var fields = (from prop in properties
                let attr = prop.GetCustomAttribute<UserInputAttribute>()!
                select new LogSystemField
                {
                    DisplayName = attr.DisplayName,
                    PropertyName = prop.Name,
                    Type = attr.InputType,
                    Watermark = attr.WaterMark,
                    Description = attr.Description,
                    IsRequired = attr.IsRequired,
                    Value = prop.GetValue(draftSettingsLogService)?.ToString()
                }).ToList();

            LogSystems.Add(new LogSystemConfig
            {
                DisplayName = classAttr.ServiceName,
                Fields = fields,
                RawType = draftSettingsLogService.GetType(),
                UploadEnabled = ((ThirdPartyLogService)draftSettingsLogService).AutoQSOUploadEnabled
            });
        }
    }

    private async Task _initializeHamlibAsync()
    {
        if (_initSkipped) return;
        var (result, output) = await _rigctldService.StartOnetimeRigctldAsync("--version");
        // init hamlib
        if (result)
        {
            HamlibVersion = output;
        }
        else
        {
            Notification?.SendErrorNotificationSync(output);
            return;
        }

        var (listResult, opt) = await _rigctldService.StartOnetimeRigctldAsync("--list");
        if (listResult)
        {
            SupportedModels = _rigctldService.ParseAllModelsFromRawOutput(opt)
                .OrderBy(x => x.Model)
                .ToList();

            var selection = DraftSettings.HamlibSettings.SelectedRigInfo;
            DraftSettings.HamlibSettings.SelectedRigInfo = null;
            if (selection is not null && SupportedModels.Contains(selection))
                DraftSettings.HamlibSettings.SelectedRigInfo = selection;
            HamlibInitPassed = true;
        }
        else
        {
            Notification?.SendErrorNotificationSync(opt);
        }
    }

    private async Task _testCloudlogConnection()
    {
        try
        {
            CloudlogInfoPanelUserControl.InfoMessage = string.Empty;
            var msg = await CloudlogUtil.TestCloudlogConnectionAsync(DraftSettings.CloudlogSettings.CloudlogUrl,
                DraftSettings.CloudlogSettings.CloudlogApiKey, _source.Token);

            if (!string.IsNullOrEmpty(msg)) throw new Exception(msg);

            var stationInfo = await CloudlogUtil.GetStationInfoAsync(DraftSettings.CloudlogSettings.CloudlogUrl,
                DraftSettings.CloudlogSettings.CloudlogApiKey, _source.Token);
            if (stationInfo.Count == 0)
            {
                DraftSettings.CloudlogSettings.AvailableCloudlogStationInfo.Clear();
                DraftSettings.CloudlogSettings.CloudlogStationInfo = null;
                ShowCloudlogStationIdCombobox = false;
                throw new Exception(TranslationHelper.GetString(LangKeys.failedstationinfo));
            }

            var oldVal = DraftSettings.CloudlogSettings.CloudlogStationInfo;

            DraftSettings.CloudlogSettings.AvailableCloudlogStationInfo.Clear();
            DraftSettings.CloudlogSettings.AvailableCloudlogStationInfo.AddRange(stationInfo);
            ShowCloudlogStationIdCombobox = true;

            if (string.IsNullOrEmpty(DraftSettings.CloudlogSettings.CloudlogStationInfo?.StationId))
            {
                DraftSettings.CloudlogSettings.CloudlogStationInfo = stationInfo[0];
            }
            else
            {
                DraftSettings.CloudlogSettings.CloudlogStationInfo = null;
                DraftSettings.CloudlogSettings.CloudlogStationInfo = oldVal;
            }

            var instType =
                await CloudlogUtil.GetCurrentServerInstanceTypeAsync(DraftSettings.CloudlogSettings.CloudlogUrl,
                    _source.Token);
            // instanceuncompitable
            if (instType != ServerInstanceType.Cloudlog)
                CloudlogInfoPanelUserControl.InfoMessage = TranslationHelper.GetString(LangKeys.instanceuncompitable)
                    .Replace("{replace01}", instType.ToString());
        }
        catch (FlurlHttpException ex) when (ex.InnerException is TaskCanceledException && _source.IsCancellationRequested)
        {
            ClassLogger.Trace("User closed setting page; test cloudlog cancelled.");
        }
    }


    private (string, int) _getRigctldIpAndPort()
    {
        // parse addr
        var ip = DefaultConfigs.RigctldDefaultHost;
        var port = DefaultConfigs.RigctldDefaultPort;

        if (DraftSettings.HamlibSettings.UseExternalRigctld)
            (ip, port) = IPAddrUtil.ParseAddress(DraftSettings.HamlibSettings.ExternalRigctldHostAddress);

        if (DraftSettings.HamlibSettings.UseRigAdvanced &&
            !string.IsNullOrEmpty(DraftSettings.HamlibSettings.OverrideCommandlineArg))
        {
            // match port
            var matchPort = Regex.Match(DraftSettings.HamlibSettings.OverrideCommandlineArg, @"-t\s+(\S+)");
            if (matchPort.Success)
            {
                port = int.Parse(matchPort.Groups[1].Value);
                ClassLogger.Debug($"Match port from args: {port}");
            }
            else
            {
                throw new Exception(TranslationHelper.GetString(LangKeys.failextractinfo));
            }
            
            // require verbose
            if (!DraftSettings.HamlibSettings.OverrideCommandlineArg.Contains("-vvvvv"))
            {
                throw new Exception(TranslationHelper.GetString(LangKeys.mustverbose));
            }
        }

        return (ip, port);
    }

    private async Task _testHamlib()
    {
        var (ip, port) = _getRigctldIpAndPort();
        if (DraftSettings.HamlibSettings is { UseExternalRigctld: false, SelectedRigInfo.Id: not null })
        {
            var defaultArgs = _rigctldService.GenerateRigctldCmdArgs(DraftSettings.HamlibSettings.SelectedRigInfo.Id,
                DraftSettings.HamlibSettings.SelectedPort);

            if (DraftSettings.HamlibSettings.UseRigAdvanced)
            {
                if (string.IsNullOrEmpty(DraftSettings.HamlibSettings.OverrideCommandlineArg))
                    defaultArgs = _rigctldService.GenerateRigctldCmdArgs(DraftSettings.HamlibSettings.SelectedRigInfo.Id,
                        DraftSettings.HamlibSettings.SelectedPort,
                        DraftSettings.HamlibSettings.DisablePTT,
                        DraftSettings.HamlibSettings.AllowExternalControl);
                else
                    defaultArgs = DraftSettings.HamlibSettings.OverrideCommandlineArg;
            }

            var (res, des) =
                await _rigctldService.RestartRigctldBackgroundProcessAsync(defaultArgs);
            if (!res && !_source.IsCancellationRequested) throw new Exception(des);
        }
        else
        {
            _rigctldService.TerminateBackgroundProcess();
        }

        _ = await _rigctldService.GetAllRigInfo(ip, port, DraftSettings.HamlibSettings.ReportRFPower,
            DraftSettings.HamlibSettings.ReportSplitInfo, _source.Token);
    }

    private async Task _refreshPort()
    {
        Ports = SerialPort.GetPortNames().ToList();
        var tmp = DraftSettings.HamlibSettings.SelectedPort;
        if (!string.IsNullOrEmpty(tmp) && !Ports.Contains(tmp)) tmp = string.Empty;
        if (string.IsNullOrEmpty(DraftSettings.HamlibSettings.SelectedPort))
            DraftSettings.HamlibSettings.SelectedPort = SerialUtil.PreSelectSerialByName();
        // reset port name
        DraftSettings.HamlibSettings.SelectedPort = "";
        DraftSettings.HamlibSettings.SelectedPort = tmp;
    }

    private void _discardConf()
    {
        // resume settings
        if (Design.IsDesignMode) return;
        _settingsService.RestoreSettings(this);
        _source.Cancel();
    }

    private void _saveAndApplyConf()
    {
        _settingsService.ApplySettings(this, LogSystems.ToList());
        _source.Cancel();
    }

    #region CloudlogAPI

    public FixedInfoPanelUserControlViewModel CloudlogInfoPanelUserControl { get; } = new();
    public TestButtonUserControlViewModel CloudlogTestButtonUserControl { get; } = new();
    [Reactive] public bool ShowCloudlogStationIdCombobox { get; set; }

    #endregion

    #region HamLib

    [Reactive] public bool HamlibInitPassed { get; set; }
    public ReactiveCommand<Unit, Unit> RefreshPort { get; }

    public TestButtonUserControlViewModel HamlibTestButtonUserControl { get; } = new();

    [Reactive] public List<string> Ports { get; set; }
    [Reactive] public string HamlibVersion { get; set; } = "Unknown hamlib version";

    [Reactive] public List<RigInfo> SupportedModels { get; set; } = new();

    #endregion
}