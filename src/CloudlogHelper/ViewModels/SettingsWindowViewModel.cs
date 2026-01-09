using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;
using CloudlogHelper.Enums;
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

namespace CloudlogHelper.ViewModels;

public class SettingsWindowViewModel : ViewModelBase
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly bool _initSkipped;
    private readonly IRigBackendManager _rigBackendManager;
    private readonly IApplicationSettingsService _settingsService;
    private readonly CancellationTokenSource _source;
    private readonly IWindowManagerService _windowManagerService;
    private readonly ICLHServerService _clhServerService;

    public SettingsWindowViewModel()
    {
        if (!Design.IsDesignMode) throw new InvalidOperationException("This should be called from designer only.");
        DraftSettings = new ApplicationSettings();
        DiscardConf = ReactiveCommand.Create(() => { });
        OpenConfDir = ReactiveCommand.Create(() => { });
        OpenTempDir = ReactiveCommand.Create(() => { });
        SaveAndApplyConf = ReactiveCommand.Create(() => { });
        HamlibInited = true;
        OmniRigInited = true;
        FullyInitialized = true;
        // InitializeLogSystems();
    }


    public SettingsWindowViewModel(CommandLineOptions cmd,
        IWindowManagerService windowManager,
        IApplicationSettingsService ss,
        IRigBackendManager rs,
        ICLHServerService cs)
    {
        _windowManagerService = windowManager;
        _settingsService = ss;
        _rigBackendManager = rs;
        _clhServerService = cs;
        
        if (!_settingsService.TryGetDraftSettings(this, out var settings))
            throw new Exception("Draft setting instance is held by another viewmodel!");
        DraftSettings = settings!;
        
        var hamlibCmd = ReactiveCommand.CreateFromTask(_testHamlib, DraftSettings.HamlibSettings.IsHamlibValid);
        HamlibTestButtonUserControl = new TestButtonUserControlViewModel(hamlibCmd);

        var flrigCmd = ReactiveCommand.CreateFromTask(_testFLRig, DraftSettings.FLRigSettings.IsFLRigValid);
        FLRigTestButtonUserControl = new TestButtonUserControlViewModel(flrigCmd);

        var omniCmd = ReactiveCommand.CreateFromTask(_testOmniRig, DraftSettings.OmniRigSettings.IsOmniRigValid);
        OmniRigTestButtonUserControl = new TestButtonUserControlViewModel(omniCmd);
        
        var clhCmd = ReactiveCommand.CreateFromTask(_testClhServer, DraftSettings.CLHServerSettings.IsCLHServerValid);
        CLHServerTestButtonUserControl = new TestButtonUserControlViewModel(clhCmd);

        var cloudCmd = ReactiveCommand.CreateFromTask(_testCloudlogConnection, DraftSettings.CloudlogSettings.IsCloudlogValid);
        CloudlogTestButtonUserControl = new TestButtonUserControlViewModel(cloudCmd);
        
        RefreshPort = ReactiveCommand.CreateFromTask(_refreshPort);
        DiscardConf = ReactiveCommand.Create(_discardConf);
        SaveAndApplyConf = ReactiveCommand.Create(_saveAndApplyConf);
        OpenConfDir = ReactiveCommand.CreateFromTask(_openConf);
        OpenTempDir = ReactiveCommand.CreateFromTask(_openTemp);

        _initSkipped = cmd.AutoUdpLogUploadOnly;
        _source = new CancellationTokenSource();
        ShowCloudlogStationIdCombobox = DraftSettings.CloudlogSettings.AvailableCloudlogStationInfo.Count > 0;
        
        LanguageInfos.AddRange(TranslationHelper.GetSupportedLanguageInfos());

        this.WhenActivated(disposables =>
        {
            // ensure rig service is not dupe
            this.WhenAnyValue(x => x.DraftSettings.FLRigSettings.PollAllowed,
                    x => x.DraftSettings.HamlibSettings.PollAllowed,
                    x => x.DraftSettings.OmniRigSettings.PollAllowed)
                .Select(values => new[] { values.Item1, values.Item2, values.Item3 })
                .Subscribe(e =>
                {
                    if (e.Count(x => x) > 1)
                    {
                        Notification?.SendWarningNotificationSync(
                            TranslationHelper.GetString(LangKeys.duperigservdetected));
                        DraftSettings.FLRigSettings.PollAllowed = false;
                        DraftSettings.OmniRigSettings.PollAllowed = false;
                    }
                }).DisposeWith(disposables);

            // Subscribe language change
            this.WhenAnyValue(x => x.DraftSettings.BasicSettings.LanguageType).Subscribe(language =>
                {
                    I18NExtension.Culture = TranslationHelper.GetCultureInfo(language);
                })
                .DisposeWith(disposables);
            hamlibCmd.ThrownExceptions.Subscribe(err => Notification?.SendErrorNotificationSync(err.Message))
                .DisposeWith(disposables);
            omniCmd.ThrownExceptions.Subscribe(err => Notification?.SendErrorNotificationSync(err.Message))
                .DisposeWith(disposables);
            flrigCmd.ThrownExceptions.Subscribe(err => Notification?.SendErrorNotificationSync(err.Message))
                .DisposeWith(disposables);
            cloudCmd.ThrownExceptions.Subscribe(err => Notification?.SendErrorNotificationSync(err.Message))
                .DisposeWith(disposables);
            clhCmd.ThrownExceptions.Subscribe(err => Notification?.SendErrorNotificationSync(err.Message))
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

        FullyInitialized = false;

        Task.WhenAll(_initializeLogSystemsAsync(), _initializeHamlibAsync(), _initializeOmniRigAsync())
            .ContinueWith((async task =>
            { 
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    FullyInitialized = true;
                });

                MessageBus.Current.SendMessage(new SettingsChanged
                {
                    Part = ChangedPart.NothingJustOpened
                });
            }));
    }

    public ObservableCollection<LogSystemConfig> LogSystems { get; } = new();

    public ReactiveCommand<Unit, Unit> SaveAndApplyConf { get; }
    public ReactiveCommand<Unit, Unit> DiscardConf { get; }
    public ReactiveCommand<Unit, Unit> OpenConfDir { get; }
    public ReactiveCommand<Unit, Unit> OpenTempDir { get; }

    public IInAppNotificationService Notification { get; set; }
    public ApplicationSettings DraftSettings { get; set; }

    private async Task _initializeLogSystemsAsync()
    {
        foreach (var draftSettingsLogService in DraftSettings.LogServices)
        {
            var classAttr = draftSettingsLogService.GetType().GetCustomAttribute<LogServiceAttribute>();
            if (classAttr == null) throw new Exception("Failed to find class attr!");

            var properties = draftSettingsLogService.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.IsDefined(typeof(UserInputAttribute), false));

            var fields = properties.Select(prop =>
                    new { prop, attr = prop.GetCustomAttribute<UserInputAttribute>()! })
                .Select(t =>
                {
                    var selections = Array.Empty<string>();

                    if (!string.IsNullOrWhiteSpace(t.attr.SelectionsArrayName))
                    {
                        var draftType = draftSettingsLogService.GetType();
                        var value = draftType
                            .GetField(t.attr.SelectionsArrayName)?
                            .GetValue(draftSettingsLogService);

                        if (value is null)
                            draftType
                                .GetProperty(t.attr.SelectionsArrayName)?
                                .GetValue(draftSettingsLogService);

                        if (value is string[] ss) selections = ss;
                    }

                    return new LogSystemField
                    {
                        DisplayNameLangKey = t.attr.DisplayNameLangKey,
                        PropertyName = t.prop.Name,
                        Type = t.attr.InputType,
                        Watermark = t.attr.WaterMark,
                        Description = t.attr.Description,
                        IsRequired = t.attr.IsRequired,
                        Selections = selections,
                        Value = t.prop.GetValue(draftSettingsLogService)?.ToString()
                    };
                }).ToList();

            LogSystems.Add(new LogSystemConfig
            {
                DisplayName = classAttr.ServiceName,
                Fields = fields,
                RawType = draftSettingsLogService.GetType(),
                UploadEnabled = draftSettingsLogService.AutoQSOUploadEnabled
            });
        }
    }

    private async Task _initializeOmniRigAsync()
    {
        try
        {
            if (_initSkipped) return;
            if (!OperatingSystem.IsWindows())
            {
                OmniRigInited = false;
                return;
                // throw new Exception("OmniRig is only supported on Windows.");
            }

            var omniRigType = Type.GetTypeFromProgID(DefaultConfigs.OmniRigEngineProgId);
            if (omniRigType is null)
            {
                OmniRigInited = false;
                return;
                // throw new Exception("OmniRig COM not found!");
            }
        }
        catch (Exception e)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { OmniRigInited = false; });
            ClassLogger.Error(e," Failed to init omnirig.");
            await Notification.SendErrorNotificationAsync(e.Message);
        }
    }

    private async Task _initializeHamlibAsync()
    {
        try
        {
            if (_initSkipped) return;
            var output = await _rigBackendManager
                .GetServiceByName(RigBackendServiceEnum.Hamlib).GetServiceVersion();
            HamlibVersion = output;

            var opt = await _rigBackendManager
                .GetServiceByName(RigBackendServiceEnum.Hamlib).GetSupportedRigModels();

            SupportedModels = opt
                .OrderBy(x => x.Model)
                .ToList();

            var selection = DraftSettings.HamlibSettings.SelectedRigInfo;
            DraftSettings.HamlibSettings.SelectedRigInfo = null;
            if (selection is not null && SupportedModels.Contains(selection))
                DraftSettings.HamlibSettings.SelectedRigInfo = selection;
        }
        catch (Exception e)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { HamlibInited = false; });
            ClassLogger.Error(e, "Failed to init hamlib.");
            await Notification.SendErrorNotificationAsync(e.Message);
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
        catch (FlurlHttpException ex) when (ex.InnerException is TaskCanceledException &&
                                            _source.IsCancellationRequested)
        {
            ClassLogger.Trace("User closed setting page; test cloudlog cancelled.");
        }
    }


    private async Task _testHamlib()
    {
        await _rigBackendManager.ExecuteTest(RigBackendServiceEnum.Hamlib, DraftSettings, _source.Token);
    }

    private async Task _testOmniRig()
    {
        await _rigBackendManager.ExecuteTest(RigBackendServiceEnum.OmniRig, DraftSettings, _source.Token);
    }

    private async Task _testFLRig()
    {
        await _rigBackendManager.ExecuteTest(RigBackendServiceEnum.FLRig, DraftSettings, _source.Token);
    }

    private async Task _testClhServer()
    {
        await _clhServerService.TestConnectionAsync(DraftSettings, true);
    }

    private async Task _refreshPort()
    {
        // return;
        Ports = SerialPort.GetPortNames().ToList();
        var tmp = DraftSettings.HamlibSettings.SelectedPort;
        if (!string.IsNullOrEmpty(tmp) && !Ports.Contains(tmp)) tmp = string.Empty;
        // if (string.IsNullOrEmpty(DraftSettings.HamlibSettings.SelectedPort))
        //     DraftSettings.HamlibSettings.SelectedPort = SerialUtil.PreSelectSerialByName();
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
        
        MessageBus.Current.SendMessage(new SettingsChanged { Part = ChangedPart.NothingJustClosed });
    }

    private async Task _openConf()
    {
        if (Design.IsDesignMode) return;
        await _windowManagerService.LaunchDir(ApplicationStartUpUtil.GetConfigDir());
    }

    private async Task _openTemp()
    {
        if (Design.IsDesignMode) return;
        await _windowManagerService.LaunchDir(DefaultConfigs.DefaultTempFilePath);
    }

    private void _saveAndApplyConf()
    {
        if (Design.IsDesignMode) return;
        _settingsService.ApplySettings(this, LogSystems.ToList());
        _source.Cancel();

        MessageBus.Current.SendMessage(new SettingsChanged { Part = ChangedPart.NothingJustClosed });
    }
    
    [Reactive] public bool FullyInitialized { get; set; } = false;
    
    public ObservableCollection<SupportedLanguageInfo> LanguageInfos { get; } = new();
    
    #region CloudlogAPI

    public FixedInfoPanelUserControlViewModel CloudlogInfoPanelUserControl { get; } = new();
    public TestButtonUserControlViewModel CloudlogTestButtonUserControl { get; }
    [Reactive] public bool ShowCloudlogStationIdCombobox { get; set; }

    #endregion

    #region HamLib

    [Reactive] public bool HamlibInited { get; set; } = true;
    public ReactiveCommand<Unit, Unit> RefreshPort { get; }

    public TestButtonUserControlViewModel HamlibTestButtonUserControl { get; }

    [Reactive] public List<string> Ports { get; set; }
    [Reactive] public string HamlibVersion { get; set; } = "Unknown hamlib version";

    [Reactive] public List<RigInfo> SupportedModels { get; set; } = new();

    #endregion

    #region OmniRig

    [Reactive] public bool OmniRigInited { get; set; } = true;

    public TestButtonUserControlViewModel OmniRigTestButtonUserControl { get; }

    #endregion

    #region FLRig

    public TestButtonUserControlViewModel FLRigTestButtonUserControl { get; }

    #endregion

    #region CLHServer
    
    public TestButtonUserControlViewModel CLHServerTestButtonUserControl { get; }
    
    #endregion
}