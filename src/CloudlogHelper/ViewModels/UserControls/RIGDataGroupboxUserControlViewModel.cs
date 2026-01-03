using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CloudlogHelper.CLHProto;
using CloudlogHelper.Enums;
using CloudlogHelper.Exceptions;
using CloudlogHelper.LogService;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;
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

    private readonly IInAppNotificationService _inAppNotification;
    private readonly IMessageBoxManagerService _messageBoxManagerService;
    private readonly IRigBackendManager _rigBackendManager;
    private readonly List<ThirdPartyLogService> _tpService;
    private readonly IWindowManagerService _windowManagerService;
    private readonly ICLHServerService _clhServerService;

    /// <summary>
    ///     observable sequence. Whether to cancel the timer or not.
    /// </summary>
    private readonly Subject<Unit> _cancelSubject = new();

    /// <summary>
    ///     Command for polling data(mode and frequency)
    /// </summary>
    private ReactiveCommand<Unit, Unit> _pollCommand;

    /// <summary>
    ///     Accumulative rig connection failed times.
    /// </summary>
    private volatile int _rigConnFailedTimes;

    /// <summary>
    ///     Lock for rig connection failed times to ensure thread safety
    /// </summary>
    private readonly object _rigConnFailedTimesLock = new();

    /// <summary>
    ///     whether to call _restartRigctldStrict or not.
    /// </summary>
    private bool _holdRigUpdate;

    /// <summary>
    ///     Composite disposable for managing timer disposables
    /// </summary>
    private readonly CompositeDisposable _timerDisposables = new();

    /// <summary>
    ///     Semaphore to prevent re-entrant polling
    /// </summary>
    private readonly SemaphoreSlim _pollLock = new(1, 1);

    public RIGDataGroupboxUserControlViewModel()
    {
        if (!Design.IsDesignMode) throw new InvalidOperationException("This should be called from designer only.");
    }

    public RIGDataGroupboxUserControlViewModel(CommandLineOptions cmd,
        IRigBackendManager rs,
        IInAppNotificationService ws,
        IWindowManagerService wm,
        IMessageBoxManagerService mm,
        ICLHServerService clh,
        IApplicationSettingsService ss)
    {
        _cloudlogSettings = ss.GetCurrentSettings().CloudlogSettings;
        _messageBoxManagerService = mm;
        _windowManagerService = wm;
        _rigBackendManager = rs;
        _inAppNotification = ws;
        _clhServerService = clh;
        _tpService = ss.GetCurrentSettings().LogServices;
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
    ///     Whether rig refresh button is cooling down or not.
    /// </summary>
    [Reactive]
    public bool IsCoolingDown { get; set; }

    [Reactive] public bool IsSplit { get; set; }
    [Reactive] public string? UploadStatus { get; set; } = TranslationHelper.GetString(LangKeys.unknown);
    [Reactive] public string? NextUploadTime { get; set; } = TranslationHelper.GetString(LangKeys.unknown);

    private void Initialize()
    {
        // check if conf is available, then start rigctld
        _pollCommand = ReactiveCommand.CreateFromTask(_refreshRigInfoSafe);

        this.WhenActivated(disposables =>
        {
            _cancelSubject.DisposeWith(disposables);

            // Handle settings changes with throttling to avoid excessive restarts
            MessageBus.Current.Listen<SettingsChanged>()
                .Throttle(TimeSpan.FromMilliseconds(500))
                .Select( x =>
                {
                    if (x.Part == ChangedPart.NothingJustOpened) _holdRigUpdate = true;

                    if (x.Part is ChangedPart.Hamlib or ChangedPart.FLRig or ChangedPart.OmniRig) 
                        _resetStatus();

                    if (x.Part == ChangedPart.NothingJustClosed)
                    {
                        lock (_rigConnFailedTimesLock)
                        {
                            _rigConnFailedTimes = 0;
                        }
                        _holdRigUpdate = false;
                    }

                    return Unit.Default;
                })
                .Subscribe(_ =>
                {
                    // poll immediately after settings changed.
                    Observable.Timer(TimeSpan.FromSeconds(2))
                        .Select(_ => Unit.Default)
                        .InvokeCommand(_pollCommand)
                        .DisposeWith(disposables);
                    
                    _createNewTimer().DisposeWith(disposables);
                })
                .DisposeWith(disposables);

            // Handle polling command exceptions
            _pollCommand.ThrownExceptions
                .Subscribe(async void (ex) => await HandlePollExceptionAsync(ex)) // this is safe - it never fails
                .DisposeWith(disposables);

            // Start the polling timer
            _createNewTimer().DisposeWith(disposables);

            // Initial poll after delay
            Observable.Return(Unit.Default)
                .Delay(TimeSpan.FromSeconds(1))
                .InvokeCommand(_pollCommand)
                .DisposeWith(disposables);
        });
    }

    private async Task _refreshRigInfoSafe()
    {
        if (!await _pollLock.WaitAsync(0))
        {
            ClassLogger.Trace("Polling already in progress, skipping...");
            return;
        }

        try
        {
            await _refreshRigInfo();
        }
        finally
        {
            _pollLock.Release();
        }
    }

    private async Task _refreshRigInfo()
    {
        ClassLogger.Trace("Refreshing rig data....");

        // Check if we should skip polling
        lock (_rigConnFailedTimesLock)
        {
            if (_rigConnFailedTimes < 0)
            {
                ClassLogger.Trace("Waiting for user choice. Skipping poll.");
                return;
            }
        }

        try
        {
            var allInfo = await _rigBackendManager.GetAllRigInfo();
            ClassLogger.Debug(allInfo.ToString());
            
            UpdateDisplayInfo(allInfo);
            
            lock (_rigConnFailedTimesLock)
            {
                _rigConnFailedTimes = 0;
            }
            
            await ReportToServicesAsync(allInfo);
        }
        catch (Exception ex) when (ex is InvalidPollException or InvalidConfigurationException)
        {
            // These are expected exceptions, just reset the counter
            lock (_rigConnFailedTimesLock)
            {
                _rigConnFailedTimes = 0;
            }
            throw;
        }
        catch (Exception ex)
        {
            lock (_rigConnFailedTimesLock)
            {
                _rigConnFailedTimes++;
            }
            throw;
        }
    }

    private void UpdateDisplayInfo(RadioData allInfo)
    {
        CurrentRxFrequency = FreqHelper.GetFrequencyStr(allInfo.FrequencyRx, false);
        CurrentRxFrequencyInMeters = FreqHelper.GetMeterFromFreq(allInfo.FrequencyRx);
        CurrentRxMode = allInfo.ModeRx;

        CurrentTxFrequency = FreqHelper.GetFrequencyStr(allInfo.FrequencyTx, false);
        CurrentTxFrequencyInMeters = FreqHelper.GetMeterFromFreq(allInfo.FrequencyTx);
        CurrentTxMode = allInfo.ModeTx;

        IsSplit = allInfo.IsSplit;
    }

    private async Task ReportToServicesAsync(RadioData allInfo)
    {
        // Report to CLH server
        await ReportToClhServerAsync(allInfo);

        // Report to third-party services
        await ReportToThirdPartyServicesAsync(allInfo);

        // Report to Cloudlog
        await ReportToCloudlogAsync(allInfo);
    }

    private async Task ReportToClhServerAsync(RadioData allInfo)
    {
        try
        {
            var rigData = new RigData
            {
                Provider = _rigBackendManager.GetServiceType().ToString(),
                RigName = allInfo.RigName,
                Frequency = SafeConvertToUlong(allInfo.FrequencyTx),
                Mode = allInfo.ModeTx,
                FrequencyRx = SafeConvertToUlong(allInfo.FrequencyRx),
                ModeRx = allInfo.ModeRx,
                Split = allInfo.IsSplit,
                Power = SafeConvertToUint(allInfo.Power ?? 0),
            };

            await _clhServerService.SendDataNoException(rigData);
        }
        catch (Exception ex)
        {
            ClassLogger.Warn(ex, "Failed to report to CLH server");
        }
    }

    private async Task ReportToThirdPartyServicesAsync(RadioData allInfo)
    {
        var uploadTasks = new List<Task>();
        
        foreach (var service in _tpService.ToArray())
        {
            uploadTasks.Add(Task.Run(async () =>
            {
                try
                {
                    await service.UploadRigInfoAsync(allInfo, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    ClassLogger.Warn(ex, "Failed to upload rig info to third-party service");
                    await _inAppNotification.SendWarningNotificationAsync(ex.Message);
                }
            }));
        }

        if (uploadTasks.Count > 0)
        {
            await Task.WhenAll(uploadTasks);
        }
    }

    private async Task ReportToCloudlogAsync(RadioData allInfo)
    {
        if (_cloudlogSettings.IsCloudlogHasErrors())
        {
            UploadStatus = TranslationHelper.GetString(LangKeys.failed);
            ClassLogger.Trace("Errors in cloudlog so ignored upload hamlib data!");
            return;
        }

        try
        {
            var result = await CloudlogUtil.UploadRigInfoAsync(
                _cloudlogSettings.CloudlogUrl,
                _cloudlogSettings.CloudlogApiKey,
                allInfo,
                CancellationToken.None);

            UploadStatus = result.Status == "success" 
                ? TranslationHelper.GetString(LangKeys.success)
                : TranslationHelper.GetString(LangKeys.failed);

            if (result.Status != "success")
            {
                throw new Exception($"Cloudlog upload failed: {result.Reason}");
            }
        }
        catch (Exception ex)
        {
            UploadStatus = TranslationHelper.GetString(LangKeys.failed);
            ClassLogger.Warn(ex, "Failed to upload rig info to Cloudlog");
            await _inAppNotification.SendWarningNotificationAsync(ex.Message);
        }
    }

    private static ulong SafeConvertToUlong(double value)
    {
        if (value <= 0) return 0;
        if (value > ulong.MaxValue) return ulong.MaxValue;
        return (ulong)value;
    }

    private static uint SafeConvertToUint(double value)
    {
        if (value <= 0) return 0;
        if (value > uint.MaxValue) return uint.MaxValue;
        return (uint)value;
    }

    private async Task HandlePollExceptionAsync(Exception exception)
    {
        if (exception is InvalidPollException or InvalidConfigurationException)
        {
            return;
        }

        try
        {
            await UpdateErrorDisplayAsync();

            int currentFailCount;
            lock (_rigConnFailedTimesLock)
            {
                currentFailCount = _rigConnFailedTimes;
            }

            if (currentFailCount >= DefaultConfigs.MaxRigctldErrorCount)
            {
                // Reset to prevent further errors while showing dialog
                lock (_rigConnFailedTimesLock)
                {
                    if (_rigConnFailedTimes >= DefaultConfigs.MaxRigctldErrorCount)
                    {
                        _rigConnFailedTimes = int.MinValue;
                    }
                }

                await ShowErrorDialogAndHandleChoiceAsync();
            }
            else
            {
                await _inAppNotification.SendErrorNotificationAsync(exception.Message);
                ClassLogger.Error(exception);
            }
        }
        catch (Exception ex)
        {
            ClassLogger.Error(ex, "Unhandled exception in poll error handler");
        }
    }

    private async Task UpdateErrorDisplayAsync()
    {
        UploadStatus = TranslationHelper.GetString(LangKeys.failed);
        CurrentRxFrequency = "ERROR";
        CurrentRxFrequencyInMeters = string.Empty;
        IsSplit = false;
        CurrentRxMode = string.Empty;
        CurrentTxFrequency = "ERROR";
        CurrentTxFrequencyInMeters = string.Empty;
        CurrentTxMode = string.Empty;
        
        await Task.CompletedTask;
    }

    private async Task ShowErrorDialogAndHandleChoiceAsync()
    {
        _disposeAllTimers();
        _resetStatus();

        var choice = await _messageBoxManagerService.DoShowCustomMessageboxDialogAsync(
            new List<ButtonDefinition>
            {
                new() { Name = "Retry", IsDefault = true },
                new() { Name = "Open Settings" },
                new() { Name = "Cancel" }
            }, 
            Icon.Warning, 
            "Warning", 
            TranslationHelper.GetString(LangKeys.failrigcomm));

        switch (choice)
        {
            case "Retry":
                lock (_rigConnFailedTimesLock)
                {
                    _rigConnFailedTimes = 0;
                }
                await _rigBackendManager.RestartService();
                Observable.Return(Unit.Default)
                    .InvokeCommand(_pollCommand);
                _createNewTimer();
                break;
                
            case "Open Settings":
                await _windowManagerService.CreateAndShowWindowByVm(typeof(SettingsWindowViewModel));
                break;
                
            case "Cancel":
                // Do nothing
                break;
        }
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

    private IDisposable _createNewTimer()
    {
        ClassLogger.Trace("Creating new rig timer...");
        
        // Cancel any existing timer
        _timerDisposables.Clear();
        _cancelSubject.OnNext(Unit.Default);

        var timerDisposable = Observable.Defer(() =>
            {
                return Observable.Interval(TimeSpan.FromSeconds(1))
                    .Scan(new { Count = 0, ShouldPoll = false }, (state, _) =>
                    {
                        var interval = _rigBackendManager.GetPollingInterval();
                        var count = (state.Count + 1) % interval;
                        var shouldPoll = count == 0;
                        
                        return new { Count = count, ShouldPoll = shouldPoll };
                    })
                    .Where(x => !_holdRigUpdate && _rigBackendManager.GetPollingAllowed())
                    .Do(state =>
                    {
                        if (!_rigBackendManager.GetPollingAllowed())
                        {
                            NextUploadTime = TranslationHelper.GetString(LangKeys.polldisabled);
                            return;
                        }

                        var interval = _rigBackendManager.GetPollingInterval();
                        var remaining = interval - state.Count - 1;
                        
                        NextUploadTime = remaining <= 0 
                            ? TranslationHelper.GetString(LangKeys.gettinginfo)
                            : remaining.ToString();
                    })
                    .Where(state => state.ShouldPoll)
                    .SelectMany(_ => _pollCommand.Execute().Catch(Observable.Empty<Unit>()))
                    .Finally(() => ClassLogger.Trace("Timer completed"));
            })
            .TakeUntil(_cancelSubject)
            .Finally(() =>
            {
                _resetStatus();
                ClassLogger.Trace("Canceling radio timer...");
            })
            .Subscribe(
                onNext: _ => { /* Timer tick handled above */ },
                onError: ex => ClassLogger.Error(ex, "Error in rig polling timer"),
                onCompleted: () => ClassLogger.Trace("Rig polling timer completed")
            );

        _timerDisposables.Add(timerDisposable);
        return timerDisposable;
    }

    private void _disposeAllTimers()
    {
        _timerDisposables.Clear();
        _cancelSubject.OnNext(Unit.Default);
    }

    // public void Dispose()
    // {
    //     _cloudlogSettings.Dispose();
    //     _cancelSubject.Dispose();
    //     _pollCommand.Dispose();
    //     _timerDisposables.Dispose();
    //     _pollLock.Dispose();
    // }
}