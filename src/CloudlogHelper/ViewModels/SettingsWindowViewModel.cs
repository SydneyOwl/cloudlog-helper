using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
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
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CloudlogHelper.Enums;
using CloudlogHelper.LogService.Attributes;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using CloudlogHelper.ViewModels.UserControls;
using DesktopNotifications;
using DynamicData;
using Flurl.Http;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;
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
    private readonly IDatabaseService _databaseService;
    private readonly IMessageBoxManagerService _messageBoxManagerService;
    private readonly ILogSystemManager _logSystemManager;

    public SettingsWindowViewModel()
    {
        if (!Design.IsDesignMode) throw new InvalidOperationException("This should be called from designer only.");
        DraftSettings = new ApplicationSettings();
        DiscardConf = ReactiveCommand.Create(() => { });
        OpenConfDir = ReactiveCommand.Create(() => { });
        OpenTempDir = ReactiveCommand.Create(() => { });
        UpdateBigCty = ReactiveCommand.Create(() => { });
        SaveAndApplyConf = ReactiveCommand.Create(() => { });
        HamlibInited = true;
        OmniRigInited = true;
        FullyInitialized = true;
        // InitializeLogSystems();
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ObservableCollection<LogSystemConfig>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ObservableCollection<SupportedLanguageInfo>))]
    public SettingsWindowViewModel(CommandLineOptions cmd,
        IWindowManagerService windowManager,
        IApplicationSettingsService ss,
        IRigBackendManager rs,
        ICLHServerService cs,
        IMessageBoxManagerService mm,
        ILogSystemManager lm,
        IDatabaseService ds)
    {
        _windowManagerService = windowManager;
        _settingsService = ss;
        _rigBackendManager = rs;
        _clhServerService = cs;
        _databaseService = ds;
        _messageBoxManagerService = mm;
        _logSystemManager = lm;
        
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
        UpdateBigCty = ReactiveCommand.CreateFromTask(_updateBigCty);

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

            var cmds = new[]
            {
                RefreshPort,
                DiscardConf,
                SaveAndApplyConf,
                OpenConfDir,
                OpenTempDir,
                UpdateBigCty,
                hamlibCmd,
                flrigCmd,
                omniCmd,
                cloudCmd,
                clhCmd,
            };
            
            foreach (var reactiveCommand in cmds)
            {
                reactiveCommand.ThrownExceptions
                    .Subscribe(err =>
                    {
                        Notification?.SendErrorNotificationSync(err.Message);
                        ClassLogger.Error(err);
                    })
                    .DisposeWith(disposables);
            }

            RefreshPort.ThrownExceptions.Subscribe(err =>
            {
                // SerialUtil.PreSelectSerialByName is likely to fail on win8 and win7!
                ClassLogger.Debug($"RefreshHamlibData error: {err.Message}; Ignored.");
            }).DisposeWith(disposables);

            // refresh hamlib lib after fully inited
            // Observable.Return(Unit.Default)
            //     .InvokeCommand(RefreshPort)
            //     .DisposeWith(disposables);
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
    public ReactiveCommand<Unit, Unit> UpdateBigCty { get; }

    public IInAppNotificationService Notification { get; set; }
    public ApplicationSettings DraftSettings { get; set; }

    private async Task _initializeLogSystemsAsync()
    {
        var config = _logSystemManager.ExtractLogSystemConfigBatch(DraftSettings.LogServices);
        if (config is null) return;
        LogSystems.AddRange(config);
    }

    private async Task _initializeOmniRigAsync()
    {
        try
        {
            if (_initSkipped) return;
            if (VersionInfo.BuildType == "AOT")
            {
                OmniRigInited = false;
                return;
            }
            
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


            DraftSettings.CloudlogSettings.AvailableCloudlogStationInfo.Clear();
            DraftSettings.CloudlogSettings.AvailableCloudlogStationInfo.AddRange(stationInfo);
            ShowCloudlogStationIdCombobox = true;
            
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

    private async Task _updateBigCty()
    {
        if (Design.IsDesignMode) return;
        try
        {
            var openFilePickerAsync = await _windowManagerService.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select cty.dat",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("dat") { Patterns = new[] { "*.dat" } }
                }
            });

            if (!openFilePickerAsync.Any()) return;
            var selected = openFilePickerAsync[0];
            if (selected is null) return;
            var stream = await selected.OpenReadAsync();
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            var (countryRow, callsignRow) = await _databaseService.UpdateCallsignAndCountry(content);
            await _messageBoxManagerService.DoShowCustomMessageboxDialogAsync(new List<ButtonDefinition>
            {
                new()
                {
                    Name = "OK",
                    IsDefault = true,
                    IsCancel = false
                }
            }, Icon.Success, TranslationHelper.GetString(LangKeys.success), 
                string.Format(TranslationHelper.GetString(LangKeys.updatebigctysuccess), 
                    callsignRow, countryRow), null);
        }
        catch (Exception ex)
        {
            await _messageBoxManagerService.DoShowCustomMessageboxDialogAsync(new List<ButtonDefinition>
                {
                    new()
                    {
                        Name = "Error",
                        IsDefault = true,
                        IsCancel = false
                    }
                }, Icon.Error, TranslationHelper.GetString(LangKeys.error), 
                string.Format(TranslationHelper.GetString(LangKeys.updatebigctyfailed), ex.Message), null);
        }
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