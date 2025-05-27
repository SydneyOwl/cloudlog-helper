using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels.UserControls;

public class RIGDataGroupboxViewModel : ViewModelBase
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    /// <summary>
    ///     Command for polling data(mode and frequency)
    /// </summary>
    private readonly ReactiveCommand<Unit, Unit> _pollCommand;

    /// <summary>
    ///     observable sequence. Whether to cancel the timer or not.
    /// </summary>
    private Subject<Unit> _cancel;

    /// <summary>
    ///     Settings for cloudlog
    /// </summary>
    private CloudlogSettings _extraSettings = ApplicationSettings.GetInstance().CloudlogSettings.DeepClone();

    /// <summary>
    ///     Accumulative failed times.
    /// </summary>
    private int _failedTimes;

    /// <summary>
    ///     whether to call _restartRigctldStrict or not.
    /// </summary>
    private bool _holdRigUpdate;

    /// <summary>
    ///     Settings for hamlib.
    /// </summary>
    private HamlibSettings _settings = ApplicationSettings.GetInstance().HamlibSettings.DeepClone();

    public RIGDataGroupboxViewModel()
    {
        // check if conf is available, then start rigctld
        _pollCommand = ReactiveCommand.CreateFromTask(_refreshRigInfo);
        this.WhenActivated(disposables =>
        {
            RigctldUtil.InitScheduler();
            _cancel = new Subject<Unit>().DisposeWith(disposables);
            MessageBus.Current.Listen<SettingsChanged>()
                .SelectMany(async x =>
                {
                    ClassLogger.Trace("Setting changed; updating hamlib info");
                    _extraSettings = ApplicationSettings.GetInstance().CloudlogSettings.DeepClone();
                    if (x.Part == ChangedPart.NothingJustOpened) _holdRigUpdate = true;

                    if (x.Part == ChangedPart.Hamlib)
                    {
                        var newSettings = ApplicationSettings.GetInstance().HamlibSettings.DeepClone();
                        if (newSettings.RestartHamlibNeeded(_settings))
                        {
                            _settings = newSettings;
                            SendMsgToParentVm("");
                            _resetStatus();
                            if (!_settings.PollAllowed)
                            {
                                // shut every service down
                                RigctldUtil.CleanUp();
                                return Unit.Default;
                            }

                            // check if we can start rigctld
                            if (_settings is { UseExternalRigctld: false })
                                await _restartRigctldStrict(false);
                            else
                                RigctldUtil.TerminateBackgroundProcess();
                        }
                        
                        _settings = newSettings;
                    }

                    if (x.Part == ChangedPart.NothingJustClosed) _holdRigUpdate = false;
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
                        _defaultExceptionHandler(err.Message);
                        if (_failedTimes++ >= DefaultConfigs.MaxRigctldErrorCount)
                        {
                            // avoid following errors
                            _failedTimes = int.MinValue;
                            // popup!
                            var choice = await ShowAskForRetryMessageBox.Handle(Unit.Default);
                            switch (choice)
                            {
                                case "Retry":
                                    _disposeAllTimers();
                                    await _restartRigctldStrict(false);
                                    _createNewTimer().DisposeWith(disposables);
                                    break;
                                case "Open Settings":
                                    await OpenSettingsWindow.Handle(Unit.Default);
                                    break;
                                case "Cancel":
                                    return;
                            }

                            _failedTimes = 0;
                        }
                    }
                    catch (Exception e)
                    {
                        // nested exception..
                        ClassLogger.Error(e.Message);
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

    /// <summary>
    ///     Open messagebox in view.
    /// </summary>
    public Interaction<Unit, string> ShowAskForRetryMessageBox { get; } = new();


    /// <summary>
    ///     Open settings in view.
    /// </summary>
    public Interaction<Unit, Unit> OpenSettingsWindow { get; } = new();

    [Reactive] public string? CurrentRxFrequency { get; set; } = "-------";
    [Reactive] public string? CurrentRxFrequencyInMeters { get; set; } = string.Empty;
    [Reactive] public string? CurrentRxMode { get; set; } = string.Empty;

    [Reactive] public string? CurrentTxFrequency { get; set; } = "-------";
    [Reactive] public string? CurrentTxFrequencyInMeters { get; set; } = string.Empty;
    [Reactive] public string? CurrentTxMode { get; set; } = string.Empty;

    [Reactive] public bool IsSplit { get; set; }
    [Reactive] public string? UploadStatus { get; set; } = TranslationHelper.GetString("unknown");
    [Reactive] public string? NextUploadTime { get; set; } = TranslationHelper.GetString("unknown");

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
                throw new Exception(TranslationHelper.GetString("failextractinfo"));
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
        UploadStatus = TranslationHelper.GetString("unknown");
        NextUploadTime = TranslationHelper.GetString("unknown");
    }

    private void _defaultExceptionHandler(string exceptionMsg)
    {
        UploadStatus = TranslationHelper.GetString("failed");
        CurrentRxFrequency = "ERROR";
        CurrentRxFrequencyInMeters = string.Empty;
        IsSplit = false;
        CurrentRxMode = string.Empty;
        SendMsgToParentVm(exceptionMsg);
        ClassLogger.Error(exceptionMsg);
    }

    private async Task _refreshRigInfo()
    {
        ClassLogger.Trace("Refreshing hamlib data....");

        if (!_settings.PollAllowed)
        {
            _failedTimes = 0;
            ClassLogger.Trace("Poll disabled. ignore....");
            return;
        }

        if (_settings.IsHamlibHasErrors())
        {
            _failedTimes = 0;
            throw new Exception(TranslationHelper.GetString("confhamlibfirst"));
        }

        // check rigctld background
        if (!_holdRigUpdate && !_settings.UseExternalRigctld) _ = await _restartRigctldStrict(true);

        // parse addr
        var (ip, port) = _getRigctldIpAndPort();
        var allInfo = await RigctldUtil.GetAllRigInfo(ip, port, _settings.ReportRFPower, _settings.ReportSplitInfo);
        ClassLogger.Debug(allInfo.ToString());
        CurrentRxFrequency = FreqHelper.GetFrequencyStr(allInfo.FrequencyRx, false);
        CurrentRxFrequencyInMeters = FreqHelper.GetMeterFromFreq(allInfo.FrequencyRx);
        CurrentRxMode = allInfo.ModeRx;

        CurrentTxFrequency = FreqHelper.GetFrequencyStr(allInfo.FrequencyTx, false);
        CurrentTxFrequencyInMeters = FreqHelper.GetMeterFromFreq(allInfo.FrequencyTx);
        CurrentTxMode = allInfo.ModeTx;

        IsSplit = allInfo.IsSplit;

        // freq read from hamlib is already in hz!
        if (!_extraSettings.IsCloudlogHasErrors())
        {
            var result = await CloudlogUtil.UploadRigInfoAsync(_extraSettings.CloudlogUrl,
                _extraSettings.CloudlogApiKey,
                _settings.SelectedRadio, allInfo);
            if (result.Status == "success")
            {
                UploadStatus = TranslationHelper.GetString("success");
            }
            else
            {
                UploadStatus = TranslationHelper.GetString("failed");
                ClassLogger.Warn($"Failed to update rig info: {result.Reason}");
            }
        }
        else
        {
            UploadStatus = TranslationHelper.GetString("failed");
            ClassLogger.Trace("Errors in cloudlog so ignored upload hamlib data!");
        }

        // then, start tcp proxy server.
        // await _restartTCPServer();

        _failedTimes = 0;

        // told parent vm cloudlog has no errors!
        SendMsgToParentVm("");
    }

    private IDisposable _createNewTimer()
    {
        ClassLogger.Trace("Creating new rig timer...");
        _cancel.OnNext(Unit.Default);

        return Observable.Defer(() =>
            {
                var pollInterval = int.Parse(_settings.PollInterval);
                return Observable.Timer(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1))
                    .Select(x => pollInterval - (int)x - 1)
                    .TakeWhile(sec => sec >= 0)
                    .Do(sec =>
                    {
                        if (!_settings.PollAllowed) return;
                        if (sec == 0)
                        {
                            NextUploadTime = TranslationHelper.GetString("gettinginfo");
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
        {
            var myRig = _settings.SelectedRadio;
            if (_settings.KnownModels.TryGetValue(myRig, out var id))
            {
                var defaultArgs = RigctldUtil.GenerateRigctldCmdArgs(id, _settings.SelectedPort);

                if (_settings.UseRigAdvanced)
                {
                    if (string.IsNullOrEmpty(_settings.OverrideCommandlineArg))
                        defaultArgs = RigctldUtil.GenerateRigctldCmdArgs(id, _settings.SelectedPort,
                            _settings.DisablePTT,
                            _settings.AllowExternalControl);
                    else
                        defaultArgs = _settings.OverrideCommandlineArg;
                }

                return (await RigctldUtil.RestartRigctldBackgroundProcessAsync(defaultArgs, ignoreIfRunning)).Item1;
            }
        }

        ClassLogger.Debug("Errors in hamlib confs. Shutting down rigctld and ignoring _restartRigctldStrict");
        RigctldUtil.TerminateBackgroundProcess();
        return false;
    }
}