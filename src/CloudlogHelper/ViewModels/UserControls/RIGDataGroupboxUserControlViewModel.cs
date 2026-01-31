using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
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
    ///     Accumulative rig connection failed times.
    /// </summary>
    private volatile int _rigConnFailedTimes;

    /// <summary>
    ///     Composite disposable for managing timer disposables
    /// </summary>
    private readonly CompositeDisposable _timerDisposables = new();

    /// <summary>
    ///     Semaphore to prevent re-entrant polling
    /// </summary>
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    
    /// <summary>
    ///     Semaphore to prevent repeated timer (polling)
    /// </summary>
    private readonly SemaphoreSlim _pollTimerLock = new(1, 1);
    

    public RIGDataGroupboxUserControlViewModel()
    {
        if (!Design.IsDesignMode) throw new InvalidOperationException("This should be called from designer only.");
        CurrentRxFrequency = "21.0742";
        CurrentRxFrequencyInMeters = "15m";
        CurrentRxMode = "USB";
        UploadStatus = RigUploadStatus.Uploading;
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
        RefreshRigInfo = ReactiveCommand.CreateFromTask(_refreshRigDataManually,this.WhenAnyValue(
            x => x.UploadStatus,
            x => x.CommStatus,
            (uploadStatus, commStatus) =>
            {
                var isUploadValid = uploadStatus != null && 
                                    uploadStatus != RigUploadStatus.Uploading && 
                                    uploadStatus != RigUploadStatus.Unknown;
                
                var isCommValid = commStatus != null && 
                                  commStatus != RigCommStatus.FetchingData && 
                                  commStatus != RigCommStatus.Unknown;
                
                return isUploadValid && isCommValid;
            }
        ));
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
    [Reactive] public bool IsSplit { get; set; }
    [Reactive] public RigUploadStatus? UploadStatus { get; set; } = RigUploadStatus.Unknown;
    [Reactive] public RigCommStatus? CommStatus { get; set; } = RigCommStatus.Unknown;
    
    
    [Reactive] public ReactiveCommand<Unit, Unit> RefreshRigInfo { get; set; }

    private async Task _refreshRigDataManually()
    {
        _disposeAllTimers();
        try
        {
            await _refreshRigInfoSafe();
        }
        catch (Exception ex)
        {
            // handler by global handler
            await HandlePollExceptionAsync(ex);
        }

        _createNewTimer(false);
    }

    private void Initialize()
    {
        this.WhenActivated(disposables =>
        {
            _cancelSubject.DisposeWith(disposables);

            MessageBus.Current.Listen<SettingsChanged>()
                .Where(x => x.Part == ChangedPart.RigService)
                .Delay(TimeSpan.FromMilliseconds(500))
                .Subscribe( x =>
                {
                    _createNewTimer().DisposeWith(disposables);
                    Interlocked.Exchange(ref  _rigConnFailedTimes, 0);
                })
                .DisposeWith(disposables);

            // Start the polling timer
            _createNewTimer().DisposeWith(disposables);
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
        if (_rigConnFailedTimes < 0)
        {
            ClassLogger.Trace("Waiting for user choice. Skipping poll.");
            return;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => CommStatus = RigCommStatus.FetchingData);
            
            var allInfo = await _rigBackendManager.GetAllRigInfo();

            await Dispatcher.UIThread.InvokeAsync(() => CommStatus = RigCommStatus.Success);
            
            UpdateDisplayInfo(allInfo);

            Interlocked.Exchange(ref _rigConnFailedTimes, 0);
            
            await ReportToServicesAsync(allInfo);
        }
        catch (Exception ex) when (ex is InvalidPollException or InvalidConfigurationException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => CommStatus = RigCommStatus.Error);
            // expected exceptions - reset the counter!
            Interlocked.Exchange(ref _rigConnFailedTimes, 0);
            throw;
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => CommStatus = RigCommStatus.Error);
            ClassLogger.Error(ex, "An error occurred while fetching rig data.");
            Interlocked.Increment(ref _rigConnFailedTimes);
            throw;
        }
    }

    private void UpdateDisplayInfo(RadioData allInfo)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            CurrentRxFrequency = FreqHelper.GetFrequencyStr(allInfo.FrequencyRx, false);
            CurrentRxFrequencyInMeters = FreqHelper.GetMeterFromFreq(allInfo.FrequencyRx);
            CurrentRxMode = allInfo.ModeRx;

            CurrentTxFrequency = FreqHelper.GetFrequencyStr(allInfo.FrequencyTx, false);
            CurrentTxFrequencyInMeters = FreqHelper.GetMeterFromFreq(allInfo.FrequencyTx);
            CurrentTxMode = allInfo.ModeTx;

            IsSplit = allInfo.IsSplit;
        });

    }

    private async Task ReportToServicesAsync(RadioData allInfo)
    {
        await Dispatcher.UIThread.InvokeAsync(() => UploadStatus = RigUploadStatus.Uploading);

        try
        {
            // Report to CLH server
            await ReportToClhServerAsync(allInfo);

            // Report to third-party services
            await ReportToThirdPartyServicesAsync(allInfo);

            // Report to Cloudlog
            await ReportToCloudlogAsync(allInfo);
            
            await Dispatcher.UIThread.InvokeAsync(() => UploadStatus = RigUploadStatus.Success);
        }
        catch (Exception ex)
        {
            ClassLogger.Warn(ex, "Error occurred while rig reporting");
            await _inAppNotification.SendWarningNotificationAsync(ex.Message);
            await Dispatcher.UIThread.InvokeAsync(() => UploadStatus = RigUploadStatus.Failed);
        }
    }

    private async Task ReportToClhServerAsync(RadioData allInfo)
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

    private async Task ReportToThirdPartyServicesAsync(RadioData allInfo)
    {
        var uploadTasks = new List<Task>();
        
        foreach (var service in _tpService.ToArray())
        {
            uploadTasks.Add(Task.Run(async () =>
            {
                await service.UploadRigInfoAsync(allInfo, CancellationToken.None);
            }));
        }

        if (uploadTasks.Count > 0)
        {
            await Task.WhenAll(uploadTasks);
        }
    }

    private async Task ReportToCloudlogAsync(RadioData allInfo)
    {
        if (!_cloudlogSettings.AutoRigUploadEnabled) return;
        
        if (_cloudlogSettings.IsCloudlogHasErrors())
        {
            ClassLogger.Trace("Errors in cloudlog so ignored upload hamlib data!");
            return;
        }

            var result = await CloudlogUtil.UploadRigInfoAsync(
                _cloudlogSettings.CloudlogUrl,
                _cloudlogSettings.CloudlogApiKey,
                allInfo,
                CancellationToken.None);


            if (result.Status != "success")
            {
                throw new Exception($"Cloudlog upload failed: {result.Reason}");
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
        if (exception is InvalidPollException) // or InvalidConfigurationException)
        {
            return;
        }

        try
        {
            await UpdateErrorDisplayAsync();

            var currentFailCount = _rigConnFailedTimes;

            if (currentFailCount >= DefaultConfigs.MaxRigctldErrorCount)
            {
                // I don't like locks - they're everywhere!
                var original = Interlocked.CompareExchange(ref _rigConnFailedTimes, int.MinValue, currentFailCount);

                if (original == currentFailCount)
                {
                    await ShowErrorDialogAndHandleChoiceAsync();
                }
            }
            else
            {
                await _inAppNotification.SendErrorNotificationAsync(exception.Message);
                ClassLogger.Error(exception, "Error while polling.");
            }
        }
        catch (Exception ex)
        {
            ClassLogger.Error(ex, "Unhandled exception in poll error handler");
        }
    }

    private async Task UpdateErrorDisplayAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentRxFrequency = "ERROR";
            CurrentRxFrequencyInMeters = string.Empty;
            IsSplit = false;
            CurrentRxMode = string.Empty;
            CurrentTxFrequency = "ERROR";
            CurrentTxFrequencyInMeters = string.Empty;
            CurrentTxMode = string.Empty;
        });
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
                Interlocked.Exchange(ref _rigConnFailedTimes, 0);
                await _rigBackendManager.RestartService();
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
        Dispatcher.UIThread.Invoke(() =>
        {
            CurrentRxFrequency = "-------";
            CurrentRxFrequencyInMeters = string.Empty;
            CurrentRxMode = string.Empty;

            CurrentTxFrequency = "-------";
            CurrentTxFrequencyInMeters = string.Empty;
            CurrentTxMode = string.Empty;

            IsSplit = false;
            UploadStatus = RigUploadStatus.Unknown;
            CommStatus = RigCommStatus.Unknown;
        });
    }
    
    private IDisposable _createNewTimer(bool withInitialPoll = true)
    {
        _disposeAllTimers();
        _pollTimerLock.Wait(CancellationToken.None);
        ClassLogger.Trace("Creating new rig timer.");
        var timerDisposable = Observable.Defer(() =>
            {
                return Observable.Create<Unit>(observer =>
                {
                    var innerCancellation = new CancellationTokenSource();
                    var innerCancellationToken = innerCancellation.Token;
                    
                    async Task RunTimerAsync()
                    {
                        if (_rigBackendManager.GetPollingAllowed() && withInitialPoll)
                        {
                            try
                            {
                                await _refreshRigInfoSafe();
                            }
                            catch (Exception ex)
                            {
                                await HandlePollExceptionAsync(ex);
                            }
                        }
                        else
                        {
                            // polling is not enabled - stop this timer immediately.
                            ClassLogger.Trace("No rig service available - polling timer stopped.");
                            observer.OnCompleted();
                            return;
                        }

                        while (!innerCancellationToken.IsCancellationRequested)
                        {
                            try
                            {
                                if (!_rigBackendManager.GetPollingAllowed())
                                {
                                    _resetStatus();
                                    // NextUploadTime = TranslationHelper.GetString(LangKeys.polldisabled);
                                    await Task.Delay(1000, innerCancellationToken);
                                    continue;
                                }

                                var interval = _rigBackendManager.GetPollingInterval();
                                
                                for (var remaining = interval - 1; remaining >= 0; remaining--)
                                {
                                    if (innerCancellationToken.IsCancellationRequested) break;

                                    // if (remaining <= 0)
                                    // {
                                    //     UploadStatus = RigUploadStatus.Uploading;
                                    // }
                                    
                                    // NextUploadTime = remaining > 0 
                                    //     ? remaining.ToString()
                                    //     : TranslationHelper.GetString(LangKeys.gettinginfo);
                                    
                                    if (remaining == 0)
                                    {
                                        try
                                        {
                                            await _refreshRigInfoSafe();
                                        }
                                        catch (Exception ex)
                                        {
                                            // handler by global handler
                                            await HandlePollExceptionAsync(ex);
                                        }
                                        
                                        break;
                                    }
                                    else
                                    {
                                        await Task.Delay(1000, innerCancellationToken);
                                    }
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }
                            catch (Exception ex)
                            {
                                ClassLogger.Error(ex, "Error in timer loop");
                                await Task.Delay(5000, innerCancellationToken);
                            }
                        }
                        
                        observer.OnCompleted();
                    }

                    var timerTask = RunTimerAsync();
                    
                    return Disposable.Create(() =>
                    {
                        ClassLogger.Trace("Calling dispose.");
                        innerCancellation.Cancel();
                        timerTask.ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
                    });
                });
            })
            .TakeUntil(_cancelSubject)
            .Finally(() =>
            {
                _resetStatus();
                _pollTimerLock.Release();
                ClassLogger.Trace("Cancelled radio timer.");
            })
            .Subscribe(
                onNext: _ => { /* Timer tick handled in the observable */ },
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