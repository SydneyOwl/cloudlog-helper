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

public class RIGDataGroupboxUserControlViewModel : ViewModelBase
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    /// <summary>
    ///     Settings for cloudlog
    /// </summary>
    private readonly CloudlogSettings
        _extraSettings = ApplicationSettings.GetInstance().CloudlogSettings.GetReference();

    /// <summary>
    ///     Command for polling data(mode and frequency)
    /// </summary>
    private ReactiveCommand<Unit, Unit> _pollCommand;

    /// <summary>
    ///     Settings for hamlib.
    /// </summary>
    private readonly HamlibSettings _settings = ApplicationSettings.GetInstance().HamlibSettings.GetReference();

    /// <summary>
    ///     observable sequence. Whether to cancel the timer or not.
    /// </summary>
    private Subject<Unit> _cancel;

    /// <summary>
    ///     whether to call _restartRigctldStrict or not.
    /// </summary>
    private bool _holdRigUpdate;


    /// <summary>
    ///     Old Settings for hamlib. Used for checking if rigctl restart is needed.
    /// </summary>
    private HamlibSettings _oldSettings = ApplicationSettings.GetInstance().HamlibSettings.DeepClone();

    /// <summary>
    ///     Accumulative rig connection failed times.
    /// </summary>
    private int _rigConnFailedTimes;
    
    /// <summary>
    /// Indicates if the initialization is skipped...
    /// </summary>
    public bool InitSkipped { get; private set; }

    private IRigctldService rigctldService;
    private IWindowManagerService windowManagerService;
    private IWindowNotificationManagerService _windowNotificationManager;
    private IMessageBoxManagerService _messageBoxManagerService;

    public RIGDataGroupboxUserControlViewModel()
    {
        if (!Design.IsDesignMode) throw new Exception("This should be called from designer only.");
    }

    public RIGDataGroupboxUserControlViewModel(CommandLineOptions cmd, 
        IRigctldService rs,
        IWindowNotificationManagerService ws,
        IWindowManagerService wm,
        IMessageBoxManagerService mm)
    {
        _messageBoxManagerService = mm;
        windowManagerService = wm;
        rigctldService = rs;
        _windowNotificationManager = ws;
        InitSkipped = cmd.AutoUdpLogUploadOnly;
        if (!InitSkipped)
        {
            Initialize(); 
        }
    }
    
    private void Initialize(){
    // check if conf is available, then start rigctld
        _pollCommand = ReactiveCommand.CreateFromTask(_refreshRigInfo);
        this.WhenActivated(disposables =>
        {
            rigctldService.InitScheduler();
            _cancel = new Subject<Unit>().DisposeWith(disposables);
            MessageBus.Current.Listen<SettingsChanged>()
                .SelectMany(async x =>
                {
                    if (x.Part == ChangedPart.NothingJustOpened) _holdRigUpdate = true;

                    if (x.Part == ChangedPart.Hamlib)
                    {
                        ClassLogger.Trace("Setting changed; updating hamlib info");
                        if (_settings.RestartHamlibNeeded(_oldSettings))
                        {
                            _resetStatus();
                            // check if we can start rigctld
                            if (_settings is { UseExternalRigctld: false })
                                await _restartRigctldStrict(false);
                            else
                                rigctldService.TerminateBackgroundProcess();
                        }

                        _oldSettings = _settings.DeepClone();
                    }

                    if (x.Part == ChangedPart.NothingJustClosed)
                    {
                        _rigConnFailedTimes = 0;
                        _holdRigUpdate = false;
                        if (!_settings.PollAllowed)
                        {
                            // shut every service down
                            rigctldService.TerminateBackgroundProcess();
                            rigctldService.TerminateOnetimeProcess();
                        }
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
                        await _defaultExceptionHandler(err.Message);
                        if (_rigConnFailedTimes++ >= DefaultConfigs.MaxRigctldErrorCount)
                        {
                            // avoid following errors
                            _rigConnFailedTimes = int.MinValue;
                            _resetStatus();
                            _disposeAllTimers();
                            // popup!
                            var choice = await _messageBoxManagerService.DoShowMessageboxAsync(new List<ButtonDefinition>
                            {
                                new() { Name = "Retry", IsDefault = true },
                                new() { Name = "Open Settings" },
                                new() { Name = "Cancel" }
                            }, Icon.Warning, "Warning", TranslationHelper.GetString(LangKeys.failrigcomm));
                            switch (choice)
                            {
                                case "Retry":
                                    _rigConnFailedTimes = 0;
                                    await _restartRigctldStrict(false);
                                    Observable.Return(Unit.Default)
                                        .InvokeCommand(_pollCommand)
                                        .DisposeWith(disposables);
                                    _createNewTimer().DisposeWith(disposables);
                                    break;
                                case "Open Settings":
                                    await windowManagerService.CreateOrShowWindowByVm(typeof(SettingsWindowViewModel));
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

            // do setup
            if (!_settings.PollAllowed) return;
            if (_settings is { UseExternalRigctld: false }) _ = _restartRigctldStrict(false);
            // poll immediately after vm inited, but wait for rigctld start.
            Observable.Return(Unit.Default)
                .Delay(TimeSpan.FromMilliseconds(1000))
                .InvokeCommand(_pollCommand)
                .DisposeWith(disposables);
        });
    }

    [Reactive] public string? CurrentRxFrequency { get; set; } = "-------";
    [Reactive] public string? CurrentRxFrequencyInMeters { get; set; } = string.Empty;
    [Reactive] public string? CurrentRxMode { get; set; } = string.Empty;

    [Reactive] public string? CurrentTxFrequency { get; set; } = "-------";
    [Reactive] public string? CurrentTxFrequencyInMeters { get; set; } = string.Empty;
    [Reactive] public string? CurrentTxMode { get; set; } = string.Empty;

    [Reactive] public bool IsSplit { get; set; }
    [Reactive] public string? UploadStatus { get; set; } = TranslationHelper.GetString(LangKeys.unknown);
    [Reactive] public string? NextUploadTime { get; set; } = TranslationHelper.GetString(LangKeys.unknown);

    // the endpoint we're sending requests to.
    // the ip is always 127.0.0.1 expect useing external rigctld.
    private (string, int) _getRigctldIpAndPort()
    {
        // parse addr
        var ip = DefaultConfigs.RigctldDefaultHost;
        var port = DefaultConfigs.RigctldDefaultPort;

        if (_settings.UseExternalRigctld) return IPAddrUtil.ParseAddress(_settings.ExternalRigctldHostAddress);

        if (_settings.UseRigAdvanced &&
            !string.IsNullOrEmpty(_settings.OverrideCommandlineArg))
        {
            var matchPort = Regex.Match(_settings.OverrideCommandlineArg, @"-t\s+(\S+)");
            if (matchPort.Success)
            {
                port = int.Parse(matchPort.Groups[1].Value);
                ClassLogger.Debug($"Match port from args: {port}");
            }
            else
            {
                throw new Exception(TranslationHelper.GetString(LangKeys.failextractinfo));
            }
        }

        return (ip, port);
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

    private async Task _defaultExceptionHandler(string exceptionMsg)
    {
        UploadStatus = TranslationHelper.GetString(LangKeys.failed);
        CurrentRxFrequency = "ERROR";
        CurrentRxFrequencyInMeters = string.Empty;
        IsSplit = false;
        CurrentRxMode = string.Empty;
        await _windowNotificationManager.SendErrorNotificationAsync(exceptionMsg);
        ClassLogger.Error(exceptionMsg);
    }

    private async Task _refreshRigInfo()
    {
        ClassLogger.Trace("Refreshing hamlib data....");

        if (_rigConnFailedTimes < 0)
        {
            ClassLogger.Trace("Waiting for user choice. ignored poll sir.");
            return;
        }

        if (!_settings.PollAllowed)
        {
            _rigConnFailedTimes = 0;
            ClassLogger.Trace("Poll disabled. ignore....");
            return;
        }

        // ClassLogger.Debug($"Let's see if its really? {_settings.SelectedRigInfo.ToString()}");

        if (_settings.IsHamlibHasErrors())
        {
            _rigConnFailedTimes = 0;
            throw new Exception(TranslationHelper.GetString(LangKeys.confhamlibfirst));
        }

        // check rigctld background
        if (!_holdRigUpdate && !_settings.UseExternalRigctld) _ = await _restartRigctldStrict(true);

        // parse addr
        var (ip, port) = _getRigctldIpAndPort();
        var allInfo = await rigctldService.GetAllRigInfo(ip, port, _settings.ReportRFPower, _settings.ReportSplitInfo, CancellationToken.None);
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
        if (!_extraSettings.IsCloudlogHasErrors())
        {
            try
            {
                var result = await CloudlogUtil.UploadRigInfoAsync(_extraSettings.CloudlogUrl,
                    _extraSettings.CloudlogApiKey,
                    _settings.SelectedRigInfo!.Model!, allInfo, CancellationToken.None);
                if (result.Status == "success")
                {
                    UploadStatus = TranslationHelper.GetString(LangKeys.success);
                }
                else
                {
                    UploadStatus = TranslationHelper.GetString(LangKeys.failed);
                    ClassLogger.Warn($"Failed to update rig info: {result.Reason}");
                }
            }
            catch (Exception ex)
            {
                UploadStatus = TranslationHelper.GetString(LangKeys.failed);
                ClassLogger.Warn(ex, "Failed to upload rig info");
            }
        }
        else
        {
            UploadStatus = TranslationHelper.GetString(LangKeys.failed);
            ClassLogger.Trace("Errors in cloudlog so ignored upload hamlib data!");
        }
    }

    private IDisposable _createNewTimer()
    {
        ClassLogger.Trace("Creating new rig timer...");
        _cancel.OnNext(Unit.Default);

        if (!int.TryParse(_settings.PollInterval, out var pollInterval))
        {
            ClassLogger.Warn(
                $"Failed to parse poll interval: {_settings.PollInterval}. Using default value {DefaultConfigs.RigctldDefaultPollingInterval}.");
            pollInterval = DefaultConfigs.RigctldDefaultPollingInterval;
        }

        return Observable.Defer(() =>
            {
                return Observable.Timer(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1))
                    .Select(x => pollInterval - (int)x - 1)
                    .TakeWhile(sec => sec >= 0)
                    .Do(sec =>
                    {
                        if (!_settings.PollAllowed || _rigConnFailedTimes < 0) return;
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

    private async Task<bool> _restartRigctldStrict(bool ignoreIfRunning)
    {
        ClassLogger.Trace("trying to restart rigctld..");
        if (!_settings.IsHamlibHasErrors())
            if (!string.IsNullOrEmpty(_settings.SelectedRigInfo?.Id))
            {
                var defaultArgs =
                    rigctldService.GenerateRigctldCmdArgs(_settings.SelectedRigInfo.Id, _settings.SelectedPort);

                if (_settings.UseRigAdvanced)
                {
                    if (string.IsNullOrEmpty(_settings.OverrideCommandlineArg))
                        defaultArgs = rigctldService.GenerateRigctldCmdArgs(_settings.SelectedRigInfo.Id,
                            _settings.SelectedPort,
                            _settings.DisablePTT,
                            _settings.AllowExternalControl);
                    else
                        defaultArgs = _settings.OverrideCommandlineArg;
                }

                return (await rigctldService.RestartRigctldBackgroundProcessAsync(defaultArgs, ignoreIfRunning)).Item1;
            }

        ClassLogger.Debug("Errors in hamlib confs. Shutting down rigctld and ignoring _restartRigctldStrict");
        rigctldService.TerminateBackgroundProcess();
        return false;
    }
}