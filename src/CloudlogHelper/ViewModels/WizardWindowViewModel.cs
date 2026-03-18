using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CloudlogHelper.Enums;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using CloudlogHelper.ViewModels.UserControls;
using Flurl.Http;
using MsBox.Avalonia.Enums;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels;

public enum WizardRigBackend
{
    None,
    Hamlib,
    FLRig,
    OmniRig
}

public sealed class RigBackendOption
{
    public WizardRigBackend Backend { get; init; }
    public string DisplayName { get; init; } = string.Empty;
}

public class WizardWindowViewModel : ViewModelBase
{
    private const int WelcomeStepIndex = 0;
    private const int BasicStepIndex = 1;
    private const int CloudlogStepIndex = 2;
    private const int ThirdPartyStepIndex = 3;
    private const int RigBackendStepIndex = 4;
    private const int TotalSteps = 5;

    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly IApplicationSettingsService _settingsService;
    private readonly IRigBackendManager _rigBackendManager;
    private readonly IMessageBoxManagerService _messageBoxManagerService;
    private readonly ILogSystemManager _logSystemManager;
    private readonly CancellationTokenSource _source = new();

    private bool _cloudlogTestPassed;
    private bool _isApplying;
    private bool _isUpdatingRigSelection;

    public WizardWindowViewModel(
        IApplicationSettingsService settingsService,
        IRigBackendManager rigBackendManager,
        IMessageBoxManagerService messageBoxManagerService,
        ILogSystemManager logSystemManager)
    {
        _settingsService = settingsService;
        _rigBackendManager = rigBackendManager;
        _messageBoxManagerService = messageBoxManagerService;
        _logSystemManager = logSystemManager;

        if (!_settingsService.TryGetDraftSettings(this, out var settings))
            throw new Exception("Draft setting instance is held by another viewmodel!");

        DraftSettings = settings!;
        EnableChartFeature = !DraftSettings.BasicSettings.DisableAllCharts;

        ConnectCloudlog = !string.IsNullOrWhiteSpace(DraftSettings.CloudlogSettings.CloudlogUrl) ||
                          !string.IsNullOrWhiteSpace(DraftSettings.CloudlogSettings.CloudlogApiKey) ||
                          DraftSettings.CloudlogSettings.AutoQSOUploadEnabled ||
                          DraftSettings.CloudlogSettings.AutoRigUploadEnabled ||
                          DraftSettings.CloudlogSettings.AutoPollStationStatus;

        _cloudlogTestPassed = DraftSettings.CloudlogSettings.AvailableCloudlogStationInfo.Count > 0;
        ShowCloudlogStationIdCombobox = _cloudlogTestPassed;

        NextCommand = ReactiveCommand.CreateFromTask(_goNextAsync);
        PreviousCommand = ReactiveCommand.Create(_goPreviousStep);
        SkipWizardCommand = ReactiveCommand.CreateFromTask(_skipWizardAndPersistAsync);
        FinishCommand = ReactiveCommand.CreateFromTask(_finishWizardAsync);
        RefreshPortCommand = ReactiveCommand.CreateFromTask(_refreshPortAsync);

        var cloudlogTestCommand = ReactiveCommand.CreateFromTask(_testCloudlogConnectionAsync);
        CloudlogTestButtonUserControl = new TestButtonUserControlViewModel(cloudlogTestCommand);

        foreach (var command in new[] { NextCommand, SkipWizardCommand, FinishCommand, RefreshPortCommand, cloudlogTestCommand })
        {
            command.ThrownExceptions.Subscribe(async ex =>
            {
                ClassLogger.Error(ex, "Wizard command failed.");
                await _showErrorAsync(ex.Message);
            });
        }

        this.WhenAnyValue(x => x.EnableChartFeature)
            .Subscribe(enabled => DraftSettings.BasicSettings.DisableAllCharts = !enabled);

        this.WhenAnyValue(x => x.ConnectCloudlog)
            .Skip(1)
            .Subscribe(enabled =>
            {
                if (enabled) return;
                _cloudlogTestPassed = false;
                ShowCloudlogStationIdCombobox = false;
                DraftSettings.CloudlogSettings.AvailableCloudlogStationInfo.Clear();
                DraftSettings.CloudlogSettings.CloudlogStationInfo = null;
            });

        this.WhenAnyValue(
                x => x.DraftSettings.CloudlogSettings.CloudlogUrl,
                x => x.DraftSettings.CloudlogSettings.CloudlogApiKey)
            .Skip(1)
            .Subscribe(_ =>
            {
                _cloudlogTestPassed = false;
                ShowCloudlogStationIdCombobox = false;
                DraftSettings.CloudlogSettings.AvailableCloudlogStationInfo.Clear();
                DraftSettings.CloudlogSettings.CloudlogStationInfo = null;
            });

        _initializeRigBackendOptions();

        this.WhenAnyValue(x => x.SelectedRigBackendOption)
            .Where(x => x is not null)
            .Subscribe(option =>
            {
                if (option is null) return;
                _applyRigBackendSelection(option.Backend);
            });

        _initializeLogSystems();
        _setInitialRigBackendSelection();
        _updateStepState(WelcomeStepIndex);

        _ = _initializeRigDataAsync();
    }

    [Reactive] public ApplicationSettings DraftSettings { get; set; }
    [Reactive] public bool EnableChartFeature { get; set; }
    [Reactive] public bool ConnectCloudlog { get; set; }
    [Reactive] public bool ShowCloudlogStationIdCombobox { get; set; }
    [Reactive] public bool HamlibInited { get; set; } = true;
    [Reactive] public bool OmniRigInited { get; set; } = true;
    [Reactive] public int CurrentStepIndex { get; private set; } = WelcomeStepIndex;
    [Reactive] public string StepIndicatorText { get; private set; } = $"1/{TotalSteps}";
    [Reactive] public bool ShowWelcomeStep { get; private set; } = true;
    [Reactive] public bool ShowBasicStep { get; private set; }
    [Reactive] public bool ShowCloudlogStep { get; private set; }
    [Reactive] public bool ShowThirdPartyStep { get; private set; }
    [Reactive] public bool ShowRigBackendStep { get; private set; }
    [Reactive] public bool ShowSkipButton { get; private set; } = true;
    [Reactive] public bool ShowPreviousButton { get; private set; }
    [Reactive] public bool ShowNextButton { get; private set; } = true;
    [Reactive] public bool ShowFinishButton { get; private set; }
    [Reactive] public bool CloseRequested { get; private set; }
    [Reactive] public bool ShowHamlibSettings { get; private set; }
    [Reactive] public bool ShowFlRigSettings { get; private set; }
    [Reactive] public bool ShowOmniRigSettings { get; private set; }
    [Reactive] public List<string> Ports { get; set; } = new();
    [Reactive] public List<RigInfo> SupportedHamlibModels { get; set; } = new();
    [Reactive] public string HamlibVersion { get; set; } = "Unknown hamlib version";
    [Reactive] public RigBackendOption? SelectedRigBackendOption { get; set; }

    public ObservableCollection<LogSystemConfig> LogSystems { get; } = new();
    public ObservableCollection<RigBackendOption> RigBackendOptions { get; } = new();

    public ReactiveCommand<Unit, Unit> NextCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviousCommand { get; }
    public ReactiveCommand<Unit, Unit> SkipWizardCommand { get; }
    public ReactiveCommand<Unit, Unit> FinishCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshPortCommand { get; }
    public TestButtonUserControlViewModel CloudlogTestButtonUserControl { get; }

    public string TitleText => TranslationHelper.GetString("wizard_title");
    public string CurrentStepTitle => CurrentStepIndex switch
    {
        WelcomeStepIndex => TranslationHelper.GetString("wizard_welcome_title"),
        BasicStepIndex => TranslationHelper.GetString("wizard_basic_title"),
        CloudlogStepIndex => TranslationHelper.GetString("wizard_cloudlog_title"),
        ThirdPartyStepIndex => TranslationHelper.GetString("wizard_thirdparty_title"),
        RigBackendStepIndex => TranslationHelper.GetString("wizard_rig_title"),
        _ => TranslationHelper.GetString("wizard_title")
    };

    public string WelcomeMessage => TranslationHelper.GetString("wizard_welcome_message");
    public string BasicStepHint => TranslationHelper.GetString("wizard_basic_hint");
    public string ChartFeatureHint => TranslationHelper.GetString("wizard_chart_feature_hint");
    public string CloudlogStepHint => TranslationHelper.GetString("wizard_cloudlog_hint");
    public string ThirdPartyStepHint => TranslationHelper.GetString("wizard_thirdparty_hint");
    public string RigStepHint => TranslationHelper.GetString("wizard_rig_hint");
    public string CloudlogConnectLabel => TranslationHelper.GetString("wizard_cloudlog_enable");
    public string CloudlogUrlLabel => TranslationHelper.GetString("wizard_cloudlog_url");
    public string CloudlogApiKeyLabel => TranslationHelper.GetString("wizard_cloudlog_apikey");
    public string CloudlogStationLabel => TranslationHelper.GetString("wizard_cloudlog_station");
    public string ChartFeatureLabel => TranslationHelper.GetString("wizard_chart_feature_label");
    public string SkipButtonText => TranslationHelper.GetString("wizard_skip");
    public string NextButtonText => TranslationHelper.GetString("wizard_next");
    public string PreviousButtonText => TranslationHelper.GetString("wizard_previous");
    public string FinishButtonText => TranslationHelper.GetString("wizard_finish");
    public string RigBackendLabel => TranslationHelper.GetString("wizard_rig_backend_label");
    public string RigUnavailableHint => TranslationHelper.GetString("wizard_omnirig_unavailable");

    public void DisposeDraftIfNeeded()
    {
        try
        {
            if (!_isApplying)
            {
                _settingsService.RestoreSettings(this);
            }
        }
        catch (Exception ex)
        {
            ClassLogger.Debug(ex, "Draft restore skipped.");
        }
        finally
        {
            _source.Cancel();
        }
    }

    private void _initializeRigBackendOptions()
    {
        RigBackendOptions.Clear();
        RigBackendOptions.Add(new RigBackendOption
        {
            Backend = WizardRigBackend.None,
            DisplayName = TranslationHelper.GetString("wizard_rig_none")
        });
        RigBackendOptions.Add(new RigBackendOption
        {
            Backend = WizardRigBackend.Hamlib,
            DisplayName = TranslationHelper.GetString("wizard_rig_hamlib")
        });
        RigBackendOptions.Add(new RigBackendOption
        {
            Backend = WizardRigBackend.FLRig,
            DisplayName = TranslationHelper.GetString("wizard_rig_flrig")
        });
        RigBackendOptions.Add(new RigBackendOption
        {
            Backend = WizardRigBackend.OmniRig,
            DisplayName = TranslationHelper.GetString("wizard_rig_omnirig")
        });
    }

    private void _initializeLogSystems()
    {
        var config = _logSystemManager.ExtractLogSystemConfigBatch(DraftSettings.LogServices);
        if (config is null) return;
        LogSystems.Clear();
        foreach (var logSystemConfig in config)
        {
            LogSystems.Add(logSystemConfig);
        }
    }

    private async Task _initializeRigDataAsync()
    {
        await _refreshPortAsync();
        await _initializeHamlibAsync();
        await _initializeOmniRigAsync();
    }

    private async Task _initializeHamlibAsync()
    {
        try
        {
            HamlibVersion = await _rigBackendManager
                .GetServiceByName(RigBackendServiceEnum.Hamlib).GetServiceVersion();

            var opt = await _rigBackendManager
                .GetServiceByName(RigBackendServiceEnum.Hamlib).GetSupportedRigModels();

            SupportedHamlibModels = opt
                .OrderBy(x => x.Model)
                .ToList();

            var selection = DraftSettings.HamlibSettings.SelectedRigInfo;
            DraftSettings.HamlibSettings.SelectedRigInfo = null;
            if (selection is not null && SupportedHamlibModels.Contains(selection))
                DraftSettings.HamlibSettings.SelectedRigInfo = selection;

            if (DraftSettings.HamlibSettings.SelectedRigInfo is null && SupportedHamlibModels.Count > 0)
                DraftSettings.HamlibSettings.SelectedRigInfo = SupportedHamlibModels.First();
        }
        catch (Exception e)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { HamlibInited = false; });
            ClassLogger.Error(e, "Failed to init hamlib in wizard.");
        }
    }

    private async Task _initializeOmniRigAsync()
    {
        try
        {
            if (VersionInfo.BuildType == "AOT" || !OperatingSystem.IsWindows())
            {
                OmniRigInited = false;
                return;
            }

            var omniRigType = Type.GetTypeFromProgID(DefaultConfigs.OmniRigEngineProgId);
            OmniRigInited = omniRigType is not null;
        }
        catch (Exception e)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { OmniRigInited = false; });
            ClassLogger.Error(e, "Failed to init omnirig in wizard.");
        }
    }

    private async Task _refreshPortAsync()
    {
        Ports = SerialPort.GetPortNames().OrderBy(x => x).ToList();
        if (string.IsNullOrWhiteSpace(DraftSettings.HamlibSettings.SelectedPort) && Ports.Count > 0)
        {
            DraftSettings.HamlibSettings.SelectedPort = Ports.First();
        }

        await Task.CompletedTask;
    }

    private async Task _goNextAsync()
    {
        switch (CurrentStepIndex)
        {
            case WelcomeStepIndex:
                _updateStepState(BasicStepIndex);
                return;
            case BasicStepIndex:
                if (!await _validateBasicStepAsync()) return;
                _updateStepState(CloudlogStepIndex);
                return;
            case CloudlogStepIndex:
                if (!await _validateCloudlogStepAsync()) return;
                _updateStepState(ThirdPartyStepIndex);
                return;
            case ThirdPartyStepIndex:
                _updateStepState(RigBackendStepIndex);
                return;
        }
    }

    private void _goPreviousStep()
    {
        if (CurrentStepIndex <= WelcomeStepIndex) return;
        _updateStepState(CurrentStepIndex - 1);
    }

    private async Task<bool> _validateBasicStepAsync()
    {
        if (string.IsNullOrWhiteSpace(DraftSettings.BasicSettings.MyMaidenheadGrid))
            return true;

        var cleanedGrid = DraftSettings.BasicSettings.MyMaidenheadGrid.Trim().ToUpperInvariant();
        DraftSettings.BasicSettings.MyMaidenheadGrid = cleanedGrid;
        if (MaidenheadGridUtil.CheckMaidenhead(cleanedGrid)) return true;

        await _showErrorAsync(TranslationHelper.GetString(LangKeys.griderror));
        return false;
    }

    private async Task<bool> _validateCloudlogStepAsync()
    {
        if (!ConnectCloudlog)
        {
            DraftSettings.CloudlogSettings.AutoQSOUploadEnabled = false;
            DraftSettings.CloudlogSettings.AutoRigUploadEnabled = false;
            DraftSettings.CloudlogSettings.AutoPollStationStatus = false;
            return true;
        }

        DraftSettings.CloudlogSettings.CloudlogUrl = DraftSettings.CloudlogSettings.CloudlogUrl.Trim();
        DraftSettings.CloudlogSettings.CloudlogApiKey = DraftSettings.CloudlogSettings.CloudlogApiKey.Trim();

        if (DraftSettings.CloudlogSettings.IsCloudlogHasErrors())
        {
            await _showErrorAsync(TranslationHelper.GetString(LangKeys.invalidconf));
            return false;
        }

        if (!_cloudlogTestPassed)
        {
            await _showErrorAsync(TranslationHelper.GetString("wizard_cloudlog_test_required"));
            return false;
        }

        if (DraftSettings.CloudlogSettings.CloudlogStationInfo is null)
        {
            await _showErrorAsync(TranslationHelper.GetString("wizard_cloudlog_station_required"));
            return false;
        }

        DraftSettings.CloudlogSettings.AutoQSOUploadEnabled = true;
        DraftSettings.CloudlogSettings.AutoRigUploadEnabled = true;
        DraftSettings.CloudlogSettings.AutoPollStationStatus = true;
        return true;
    }

    private async Task _testCloudlogConnectionAsync()
    {
        if (!ConnectCloudlog) return;

        try
        {
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
                _cloudlogTestPassed = false;
                throw new Exception(TranslationHelper.GetString(LangKeys.failedstationinfo));
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                DraftSettings.CloudlogSettings.AvailableCloudlogStationInfo.Clear();
                foreach (var station in stationInfo)
                {
                    DraftSettings.CloudlogSettings.AvailableCloudlogStationInfo.Add(station);
                }

                if (DraftSettings.CloudlogSettings.CloudlogStationInfo is null)
                    DraftSettings.CloudlogSettings.CloudlogStationInfo = stationInfo.First();

                ShowCloudlogStationIdCombobox = true;
                _cloudlogTestPassed = true;
            });

            var instType =
                await CloudlogUtil.GetCurrentServerInstanceTypeAsync(DraftSettings.CloudlogSettings.CloudlogUrl,
                    _source.Token);
            ClassLogger.Info($"Detected instance {instType}");
        }
        catch (FlurlHttpException ex) when (ex.InnerException is TaskCanceledException &&
                                            _source.IsCancellationRequested)
        {
            ClassLogger.Trace("Wizard cloudlog test cancelled.");
        }
    }

    private async Task _finishWizardAsync()
    {
        if (CurrentStepIndex != RigBackendStepIndex) return;
        if (!await _validateRigBackendStepAsync()) return;

        DraftSettings.SkipWizard = true;
        if (!ConnectCloudlog)
        {
            DraftSettings.CloudlogSettings.AutoQSOUploadEnabled = false;
            DraftSettings.CloudlogSettings.AutoRigUploadEnabled = false;
            DraftSettings.CloudlogSettings.AutoPollStationStatus = false;
        }

        _settingsService.ApplySettings(this, LogSystems.ToList());
        _isApplying = true;
        _source.Cancel();
        CloseRequested = true;
    }

    private async Task _skipWizardAndPersistAsync()
    {
        if (_isApplying) return;

        DraftSettings.SkipWizard = true;
        DraftSettings.CloudlogSettings.AutoQSOUploadEnabled = false;
        DraftSettings.CloudlogSettings.AutoRigUploadEnabled = false;
        DraftSettings.CloudlogSettings.AutoPollStationStatus = false;

        _settingsService.ApplySettings(this, LogSystems.ToList());
        _isApplying = true;
        _source.Cancel();
        CloseRequested = true;
        await Task.CompletedTask;
    }

    private async Task<bool> _validateRigBackendStepAsync()
    {
        var backend = SelectedRigBackendOption?.Backend ?? WizardRigBackend.None;
        switch (backend)
        {
            case WizardRigBackend.Hamlib:
                if (!HamlibInited || DraftSettings.HamlibSettings.IsHamlibHasErrors())
                {
                    await _showErrorAsync(TranslationHelper.GetString(LangKeys.confhamlibfirst));
                    return false;
                }

                break;
            case WizardRigBackend.FLRig:
                if (DraftSettings.FLRigSettings.IsFLRigHasErrors())
                {
                    await _showErrorAsync(TranslationHelper.GetString(LangKeys.confflrigfirst));
                    return false;
                }

                break;
            case WizardRigBackend.OmniRig:
                if (!OmniRigInited)
                {
                    await _showErrorAsync(TranslationHelper.GetString("wizard_omnirig_unavailable"));
                    return false;
                }

                if (DraftSettings.OmniRigSettings.IsOmniRigHasErrors())
                {
                    await _showErrorAsync(TranslationHelper.GetString(LangKeys.confomnifirst));
                    return false;
                }

                break;
        }

        return true;
    }

    private void _setInitialRigBackendSelection()
    {
        var initial = WizardRigBackend.None;
        if (DraftSettings.HamlibSettings.PollAllowed) initial = WizardRigBackend.Hamlib;
        else if (DraftSettings.FLRigSettings.PollAllowed) initial = WizardRigBackend.FLRig;
        else if (DraftSettings.OmniRigSettings.PollAllowed) initial = WizardRigBackend.OmniRig;

        SelectedRigBackendOption = RigBackendOptions.First(x => x.Backend == initial);
        _applyRigBackendSelection(initial);
    }

    private void _applyRigBackendSelection(WizardRigBackend backend)
    {
        if (_isUpdatingRigSelection) return;

        if (backend == WizardRigBackend.OmniRig && !OmniRigInited)
        {
            _isUpdatingRigSelection = true;
            SelectedRigBackendOption = RigBackendOptions.First(x => x.Backend == WizardRigBackend.None);
            _isUpdatingRigSelection = false;
            backend = WizardRigBackend.None;
        }

        ShowHamlibSettings = backend == WizardRigBackend.Hamlib;
        ShowFlRigSettings = backend == WizardRigBackend.FLRig;
        ShowOmniRigSettings = backend == WizardRigBackend.OmniRig;

        DraftSettings.HamlibSettings.PollAllowed = backend == WizardRigBackend.Hamlib;
        DraftSettings.FLRigSettings.PollAllowed = backend == WizardRigBackend.FLRig;
        DraftSettings.OmniRigSettings.PollAllowed = backend == WizardRigBackend.OmniRig;

        if (backend == WizardRigBackend.Hamlib)
        {
            if (DraftSettings.HamlibSettings.SelectedRigInfo is null && SupportedHamlibModels.Count > 0)
                DraftSettings.HamlibSettings.SelectedRigInfo = SupportedHamlibModels.First();

            if (string.IsNullOrWhiteSpace(DraftSettings.HamlibSettings.SelectedPort) && Ports.Count > 0)
                DraftSettings.HamlibSettings.SelectedPort = Ports.First();
        }
    }

    private void _updateStepState(int step)
    {
        CurrentStepIndex = step;
        StepIndicatorText = $"{step + 1}/{TotalSteps}";

        ShowWelcomeStep = step == WelcomeStepIndex;
        ShowBasicStep = step == BasicStepIndex;
        ShowCloudlogStep = step == CloudlogStepIndex;
        ShowThirdPartyStep = step == ThirdPartyStepIndex;
        ShowRigBackendStep = step == RigBackendStepIndex;

        ShowSkipButton = step == WelcomeStepIndex;
        ShowPreviousButton = step != WelcomeStepIndex;
        ShowNextButton = step != RigBackendStepIndex;
        ShowFinishButton = step == RigBackendStepIndex;

        this.RaisePropertyChanged(nameof(CurrentStepTitle));
    }

    private async Task _showErrorAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        await _messageBoxManagerService.DoShowStandardMessageboxDialogAsync(Icon.Error, ButtonEnum.Ok,
            TranslationHelper.GetString(LangKeys.error), message);
    }
}
