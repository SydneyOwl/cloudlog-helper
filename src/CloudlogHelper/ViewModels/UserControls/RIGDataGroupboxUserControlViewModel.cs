using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CloudlogHelper.Enums;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using Flurl.Http;
using Force.DeepCloner;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;
using Newtonsoft.Json;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels.UserControls;

public class RIGDataGroupboxUserControlViewModel : FloatableViewModelBase
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    /// <summary>
    ///     Settings for cloudlog
    /// </summary>
    private readonly CloudlogSettings _cloudlogSettings;

    private readonly IMessageBoxManagerService _messageBoxManagerService;

    /// <summary>
    ///     Settings for hamlib.
    /// </summary>
    private readonly HamlibSettings _hamlibSettings;

    /// <summary>
    ///     Target address for rig info syncing.
    /// </summary>
    private readonly List<string> _syncRigInfoAddr = new();

    private readonly IInAppNotificationService _inAppNotification;

    private readonly IRigBackendManager _rigBackendManager;
    private readonly IWindowManagerService _windowManagerService;
    private IApplicationSettingsService _applicationSettingsService;


    /// <summary>
    ///     observable sequence. Whether to cancel the timer or not.
    /// </summary>
    private Subject<Unit> _cancel;

    /// <summary>
    ///     whether to call _restartRigctldStrict or not.
    /// </summary>
    private bool _holdRigUpdate;

    /// <summary>
    ///     Command for polling data(mode and frequency)
    /// </summary>
    private ReactiveCommand<Unit, Unit> _pollCommand;

    /// <summary>
    ///     Accumulative rig connection failed times.
    /// </summary>
    private int _rigConnFailedTimes;

    public RIGDataGroupboxUserControlViewModel()
    {
        if (!Design.IsDesignMode) throw new InvalidOperationException("This should be called from designer only.");
    }

    public RIGDataGroupboxUserControlViewModel(CommandLineOptions cmd,
        IRigBackendManager rs,
        IInAppNotificationService ws,
        IWindowManagerService wm,
        IMessageBoxManagerService mm,
        IApplicationSettingsService ss)
    {
        _cloudlogSettings = ss.GetCurrentSettings().CloudlogSettings;
        _hamlibSettings = ss.GetCurrentSettings().HamlibSettings;
        _messageBoxManagerService = mm;
        _applicationSettingsService = ss;
        _windowManagerService = wm;
        _rigBackendManager = rs;
        _inAppNotification = ws;
        InitSkipped = cmd.AutoUdpLogUploadOnly;
        _ = rs.InitializeAsync();
        if (!InitSkipped) Initialize();
    }

    /// <summary>
    ///     Indicates if the initialization is skipped...
    /// </summary>
    public bool InitSkipped { get; }

    [Reactive] public string? CurrentRxFrequency { get; set; } = "-------";
    [Reactive] public string? CurrentRxFrequencyInMeters { get; set; } = string.Empty;
    [Reactive] public string? CurrentRxMode { get; set; } = string.Empty;

    [Reactive] public string? CurrentTxFrequency { get; set; } = "-------";
    [Reactive] public string? CurrentTxFrequencyInMeters { get; set; } = string.Empty;
    [Reactive] public string? CurrentTxMode { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether rig refresh button is cooling down or not.
    /// </summary>
    [Reactive] public bool IsCoolingDown { get; set; }

    [Reactive] public bool IsSplit { get; set; }
    [Reactive] public string? UploadStatus { get; set; } = TranslationHelper.GetString(LangKeys.unknown);
    [Reactive] public string? NextUploadTime { get; set; } = TranslationHelper.GetString(LangKeys.unknown);
    

    private void Initialize()
    {
        // check if conf is available, then start rigctld
        _pollCommand = ReactiveCommand.CreateFromTask(() => _refreshRigInfo());
        
        this.WhenActivated(disposables =>
        {
            _syncRigInfoAddr.AddRange(_hamlibSettings.SyncRigInfoAddress.Split(";"));
            _cancel = new Subject<Unit>().DisposeWith(disposables);
            MessageBus.Current.Listen<SettingsChanged>()
                .SelectMany(async x =>
                {
                    if (x.Part == ChangedPart.NothingJustOpened) _holdRigUpdate = true;

                    if (x.Part == ChangedPart.Hamlib)
                    {
                        if (_applicationSettingsService.RestartHamlibNeeded()) _resetStatus();

                        _syncRigInfoAddr.Clear();
                        _syncRigInfoAddr.AddRange(_hamlibSettings.SyncRigInfoAddress.Split(";"));
                    }

                    if (x.Part == ChangedPart.NothingJustClosed)
                    {
                        _rigConnFailedTimes = 0;
                        _holdRigUpdate = false;
                    }

                    return Unit.Default;
                })
                .Subscribe(_ =>
                {
                    // poll immediately after settings changed.
                    Observable.Return(Unit.Default)
                        .InvokeCommand(_pollCommand)
                        .DisposeWith(disposables);
                    _createNewTimer().DisposeWith(disposables);
                })
                .DisposeWith(disposables);

           

            _pollCommand.ThrownExceptions.Subscribe(async void (err) =>
                {
                    try
                    {
                        await _defaultExceptionHandler(err);
                        if (_rigConnFailedTimes++ >= DefaultConfigs.MaxRigctldErrorCount)
                        {
                            // avoid following errors
                            _rigConnFailedTimes = int.MinValue;
                            _resetStatus();
                            _disposeAllTimers();
                            // popup!
                            var choice = await _messageBoxManagerService.DoShowCustomMessageboxDialogAsync(
                                new List<ButtonDefinition>
                                {
                                    new() { Name = "Retry", IsDefault = true },
                                    new() { Name = "Open Settings" },
                                    new() { Name = "Cancel" }
                                }, Icon.Warning, "Warning", TranslationHelper.GetString(LangKeys.failrigcomm));
                            switch (choice)
                            {
                                case "Retry":
                                    _rigConnFailedTimes = 0;
                                    await _rigBackendManager.RestartService();
                                    Observable.Return(Unit.Default)
                                        .InvokeCommand(_pollCommand)
                                        .DisposeWith(disposables);
                                    _createNewTimer().DisposeWith(disposables);
                                    break;
                                case "Open Settings":
                                    await _windowManagerService.CreateAndShowWindowByVm(typeof(SettingsWindowViewModel));
                                    break;
                                case "Cancel":
                                    break;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // nested exception..
                        ClassLogger.Error(e);
                    }
                })
                .DisposeWith(disposables);

            _createNewTimer().DisposeWith(disposables);

            Observable.Return(Unit.Default)
                .Delay(TimeSpan.FromMilliseconds(1000))
                .InvokeCommand(_pollCommand)
                .DisposeWith(disposables);
        });
    }
    private void _resetStatus()
    {
        CurrentRxFrequency = "-------";
        CurrentRxFrequencyInMeters = string.Empty;
        CurrentRxMode = string.Empty;

        CurrentTxFrequency = "-------";
        CurrentTxFrequencyInMeters = string.Empty;
        CurrentTxMode = string.Empty;

        IsSplit = false;
        UploadStatus = TranslationHelper.GetString(LangKeys.unknown);
        NextUploadTime = TranslationHelper.GetString(LangKeys.unknown);
    }

    private async Task _defaultExceptionHandler(Exception exception)
    {
        UploadStatus = TranslationHelper.GetString(LangKeys.failed);
        CurrentRxFrequency = "ERROR";
        CurrentRxFrequencyInMeters = string.Empty;
        IsSplit = false;
        CurrentRxMode = string.Empty;
        await _inAppNotification.SendErrorNotificationAsync(exception.Message);
        ClassLogger.Error(exception);
    }

    private async Task _refreshRigInfo(bool force = false)
    {
        ClassLogger.Trace("Refreshing hamlib data....");

        if (_rigConnFailedTimes < 0 && !force)
        {
            ClassLogger.Trace("Waiting for user choice. ignored poll sir.");
            return;
        }

        if (!_hamlibSettings.PollAllowed && !force)
        {
            _rigConnFailedTimes = 0;
            ClassLogger.Trace("Poll disabled. ignore....");
            return;
        }

        if (_hamlibSettings.IsHamlibHasErrors())
        {
            _rigConnFailedTimes = 0;
            throw new Exception(TranslationHelper.GetString(LangKeys.confhamlibfirst));
        }

        var allInfo = await _rigBackendManager.GetAllRigInfo();
        ClassLogger.Debug(allInfo.ToString());
        CurrentRxFrequency = FreqHelper.GetFrequencyStr(allInfo.FrequencyRx, false);
        CurrentRxFrequencyInMeters = FreqHelper.GetMeterFromFreq(allInfo.FrequencyRx);
        CurrentRxMode = allInfo.ModeRx;

        CurrentTxFrequency = FreqHelper.GetFrequencyStr(allInfo.FrequencyTx, false);
        CurrentTxFrequencyInMeters = FreqHelper.GetMeterFromFreq(allInfo.FrequencyTx);
        CurrentTxMode = allInfo.ModeTx;

        IsSplit = allInfo.IsSplit;

        _rigConnFailedTimes = 0;

        // freq read from hamlib is already in hz!
        if (!_cloudlogSettings.IsCloudlogHasErrors())
        {
            try
            {
                var result = await CloudlogUtil.UploadRigInfoAsync(_cloudlogSettings.CloudlogUrl,
                    _cloudlogSettings.CloudlogApiKey,
                    _hamlibSettings.SelectedRigInfo!.Model!, allInfo, CancellationToken.None);
                if (result.Status == "success")
                    UploadStatus = TranslationHelper.GetString(LangKeys.success);
                else
                    throw new Exception(result.Reason);
            }
            catch (Exception ex)
            {
                UploadStatus = TranslationHelper.GetString(LangKeys.failed);
                ClassLogger.Warn(ex, "Failed to upload rig info");
                await _inAppNotification.SendWarningNotificationAsync(ex.Message);
            }
        }
        else
        {
            UploadStatus = TranslationHelper.GetString(LangKeys.failed);
            ClassLogger.Trace("Errors in cloudlog so ignored upload hamlib data!");
        }

        // finally we sync rig info to user-specified addresses.
        foreach (var se in _syncRigInfoAddr)
            try
            {
                if (string.IsNullOrWhiteSpace(se)) continue;
                await UploadRigInfoToUserSpecifiedAddressAsync(se, _hamlibSettings.SelectedRigInfo!.Model!, allInfo,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                ClassLogger.Error(ex, "Failed to upload rig info");
                await _inAppNotification.SendErrorNotificationAsync(TranslationHelper
                    .GetString(LangKeys.failuploadriginfoto).Replace("{addr}", se));
            }
    }

    private IDisposable _createNewTimer()
    {
        ClassLogger.Trace("Creating new rig timer...");
        _cancel.OnNext(Unit.Default);

        if (!int.TryParse(_hamlibSettings.PollInterval, out var pollInterval))
        {
            ClassLogger.Warn(
                $"Failed to parse poll interval: {_hamlibSettings.PollInterval}. Using default value {DefaultConfigs.RigctldDefaultPollingInterval}.");
            pollInterval = DefaultConfigs.RigctldDefaultPollingInterval;
        }

        return Observable.Defer(() =>
            {
                return Observable.Timer(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1))
                    .Select(x => pollInterval - (int)x - 1)
                    .TakeWhile(sec => sec >= 0)
                    .Do(sec =>
                    {
                        if (!_hamlibSettings.PollAllowed || _rigConnFailedTimes < 0) return;
                        if (sec == 0)
                        {
                            NextUploadTime = TranslationHelper.GetString(LangKeys.gettinginfo);
                            return;
                        }

                        NextUploadTime = sec.ToString();
                    })
                    .SelectMany(sec =>
                        sec == 0
                            ? _pollCommand.Execute().OnErrorResumeNext(Observable.Empty<Unit>())
                            : Observable.Return(Unit.Default)
                    )
                    .Finally(() => ClassLogger.Trace("Reboot radio timer.."));
            })
            .Repeat()
            .TakeUntil(_cancel)
            .Finally(() => ClassLogger.Trace("Canceling radio timer..."))
            .Subscribe();
    }

    private void _disposeAllTimers()
    {
        _cancel.OnNext(Unit.Default);
    }
    
    public async Task UploadRigInfoToUserSpecifiedAddressAsync(string url, string rigName,
        RadioData data, CancellationToken token)
    {
        var payloadI = new RadioApiCallV2
        {
            Radio = rigName,
            Frequency = data.FrequencyTx,
            Mode = data.ModeTx,
            FrequencyRx = data.FrequencyRx,
            ModeRx = data.ModeRx,
            Power = data.Power
        };
        
        var results = await url
            .WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
            .WithHeader("Content-Type", "application/json")
            .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.DefaultRequestTimeout))
            .PostStringAsync(JsonConvert.SerializeObject(payloadI), cancellationToken: token)
            .ReceiveString();

        if (results != "OK")
            throw new Exception($"Result does not return as expected: expect \"OK\" but we got {results}");
    }
}