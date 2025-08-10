using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CloudlogHelper.Database;
using CloudlogHelper.LogService;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using DynamicData;
using DynamicData.Binding;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using WsjtxUtilsPatch.WsjtxMessages.Messages;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace CloudlogHelper.ViewModels.UserControls;

public class UDPLogInfoGroupboxViewModel : ViewModelBase
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    /// <summary>
    ///     All qsologged message received.
    /// </summary>
    private readonly SourceList<RecordedCallsignDetail> _allQsos = new();

    /// <summary>
    ///     Settings for cloudlog.
    /// </summary>
    private readonly CloudlogSettings _extraCloudlogSettings =
        ApplicationSettings.GetInstance().CloudlogSettings.GetReference();

    /// <summary>
    ///     Settings for log services.
    /// </summary>
    private readonly List<ThirdPartyLogService> _logServices =
        ApplicationSettings.GetInstance().LogServices;

    /// <summary>
    ///     UDP Timeout watchdog.
    /// </summary>
    private readonly Subject<Unit> _heartbeatSubject = new();

    /// <summary>
    ///     check if this queue is empty. Reupload qso function is disabled if queue is not empty.
    /// </summary>
    private readonly BehaviorSubject<bool> _isUploadQueueEmpty;

    /// <summary>
    ///     Settings for UDPServer.
    /// </summary>
    private readonly UDPServerSettings _settings = ApplicationSettings.GetInstance().UDPSettings.GetReference();

    /// <summary>
    ///     To be uploaded QSOs queue.
    /// </summary>
    private readonly ConcurrentQueue<RecordedCallsignDetail> _uploadQueue = new();

    private readonly ReactiveCommand<Unit, Unit> RestartUdpCommand;
    private readonly ReactiveCommand<Unit, Unit> UploadLogFromQueueCommand;

    /// <summary>
    ///     Total decoded number.
    /// </summary>
    private uint _allDecodedCount;

    /// <summary>
    ///     Old Settings for UDPServer to check if a restart is needed.
    /// </summary>
    private UDPServerSettings _oldSettings = ApplicationSettings.GetInstance().UDPSettings.DeepClone();

    /// <summary>
    ///     The number of Qso made.
    /// </summary>
    private uint _qsosCount;

    public UDPLogInfoGroupboxViewModel()
    {
        _isUploadQueueEmpty = new BehaviorSubject<bool>(_uploadQueue.IsEmpty);
        ShowFilePickerDialog = new Interaction<Unit, IStorageFile?>();
        WaitFirstConn = _settings.EnableUDPServer;

        SelectAllCommand = ReactiveCommand.Create(() => { SelectAll = !SelectAll; });
        ReuploadSelectedCommand = ReactiveCommand.CreateFromTask(_uploadCheckedQSO, _isUploadQueueEmpty.AsObservable());
        ExportSelectedToAdiCommand = ReactiveCommand.CreateFromTask(_createAdifFromCheckedQSO);
        IgnoreSelectedPermanentlyCommand = ReactiveCommand.CreateFromTask(_ignoreSelectedQSO);
        DeleteSelectedCommand = ReactiveCommand.Create(() =>
        {
            _allQsos.Edit(innerList =>
            {
                for (var i = innerList.Count - 1; i >= 0; i--)
                    if (innerList[i].Checked)
                        innerList.RemoveAt(i);
            });
        });
        RestartUdpCommand = ReactiveCommand.CreateFromTask(_restartUdp);
        UploadLogFromQueueCommand = ReactiveCommand.CreateFromTask(_uploadQSOFromQueue);

        this.WhenActivated(disposables =>
        {
            // Our client watchdog
            _heartbeatSubject
                .Throttle(TimeSpan.FromSeconds(DefaultConfigs.UDPClientExpiryInSeconds))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    if (!_settings.EnableUDPServer || WaitFirstConn) return;
                    TimeoutStatus = true;
                })
                .DisposeWith(disposables);

            // Qso rate calculator
            _allQsos.Connect()
                .Buffer(TimeSpan.FromMinutes(1))
                .Select(buffer => buffer.Count)
                .StartWith(0, 0, 0, 0, 0)
                .Buffer(5, 1)
                .Select(window => window.Average())
                .Subscribe(avg => { QsAvgMin = $"{avg.ToString("0.00")}Qsos/min"; });

            var filterObservable = this.WhenAnyValue(x => x.ShowFailedOnly)
                .Select(showFailed => (Func<RecordedCallsignDetail, bool>)(detail =>
                    (detail.UploadStatus != UploadStatus.Success && showFailed) ||
                    !showFailed));

            _allQsos.Connect()
                .Filter(filterObservable)
                .Sort(SortExpressionComparer<RecordedCallsignDetail>
                    .Ascending(x => x.DateTimeOff))
                .Bind(FilteredQsos)
                .Subscribe()
                .DisposeWith(disposables);

            // Callsign update trigger
            this.WhenAnyValue(x => x.SelectAll, x => x.ShowFailedOnly)
                .Subscribe(res =>
                {
                    _allQsos.Edit(innerList =>
                    {
                        for (var i = innerList.Count - 1; i >= 0; i--)
                        {
                            if (ShowFailedOnly)
                                if (innerList[i].UploadStatus == UploadStatus.Success)
                                {
                                    innerList[i].Checked = false;
                                    continue;
                                }

                            innerList[i].Checked = SelectAll;
                        }
                    });
                }).DisposeWith(disposables);

            RestartUdpCommand.ThrownExceptions.Subscribe(async void (err) =>
                    await App.NotificationManager.SendErrorNotificationAsync(err.Message))
                .DisposeWith(disposables);
            
            IgnoreSelectedPermanentlyCommand.ThrownExceptions.Subscribe(async void (err) =>
                    await App.NotificationManager.SendErrorNotificationAsync(err.Message))
                .DisposeWith(disposables);

            UploadLogFromQueueCommand.ThrownExceptions.Subscribe(async void (err) =>
                    await App.NotificationManager.SendErrorNotificationAsync(err.Message))
                .DisposeWith(disposables);

            ExportSelectedToAdiCommand.ThrownExceptions.Subscribe(async void (err) =>
                    await App.NotificationManager.SendErrorNotificationAsync(err.Message))
                .DisposeWith(disposables);

            // refresh cloudlog infos immediately if settings changed.
            MessageBus.Current.Listen<SettingsChanged>()
                // .Where(x => x.Part == ChangedPart.UDPServer)
                .Subscribe(x =>
                {
                    if (x.Part == ChangedPart.UDPServer)
                    {
                        ClassLogger.Debug("Setting changed; updating udp");
                        WaitFirstConn = _settings.EnableUDPServer;
                        if (_settings.RestartUDPNeeded(_oldSettings)) TryStartUdpService().DisposeWith(disposables);

                        _oldSettings = _settings.DeepClone();
                    }
                })
                .DisposeWith(disposables);
            
            MessageBus.Current.Listen<QsoUploadRequested>()
                .Subscribe(x =>
                {
                    foreach (var rcd in x.QsoData)
                    {
                        _allQsos.Add(rcd);
                        _checkAndEnqueueQSO(rcd);
                    }
                })
                .DisposeWith(disposables);
            
            
            TryStartUdpService().DisposeWith(disposables);

            // start uploading service
            Observable.Return(Unit.Default).InvokeCommand(UploadLogFromQueueCommand).DisposeWith(disposables);
        });
    }

    /// <summary>
    ///     Qsos being flitered.
    /// </summary>
    public ObservableCollectionExtended<RecordedCallsignDetail> FilteredQsos { get; set; } = new();

    public ReactiveCommand<Unit, Unit> SelectAllCommand { get; set; }
    public ReactiveCommand<Unit, Unit> DeleteSelectedCommand { get; set; }
    public ReactiveCommand<Unit, Unit> ReuploadSelectedCommand { get; set; }
    public ReactiveCommand<Unit, Unit> ExportSelectedToAdiCommand { get; set; }
    public ReactiveCommand<Unit, Unit> IgnoreSelectedPermanentlyCommand { get; set; }
    

    public Interaction<Unit, IStorageFile?> ShowFilePickerDialog { get; }

    [Reactive] public bool TimeoutStatus { get; set; }
    [Reactive] public bool WaitFirstConn { get; set; }
    [Reactive] public bool TxStatus { get; set; }
    [Reactive] public string MsgSending { get; set; }


    [Reactive] public string? QsAvgMin { get; set; } = "0Qsos/min";
    [Reactive] public string QsosCountData { get; set; } = "0/0"; // total/qsos

    [Reactive] public bool SelectAll { get; set; }
    [Reactive] public bool ShowFailedOnly { get; set; }


    private IDisposable TryStartUdpService()
    {
        if (!_settings.EnableUDPServer)
        {
            UDPServerUtil.TerminateUDPServer();
            return Disposable.Empty;
        }

        return Observable.Return(Unit.Default)
            .InvokeCommand(RestartUdpCommand);
    }

    private async Task _restartUdp()
    {
        ClassLogger.Debug("trying to start UDP...");
        // await Task.Delay(500); //dirty... Validation part in Settings(init) is not ready yet so wait for 500ms
        // create a default handler here
        if (_settings.IsUDPConfigHasErrors())
        {
            UDPServerUtil.TerminateUDPServer();
            WaitFirstConn = false;
            throw new Exception(TranslationHelper.GetString("invalidudpconf"));
        }

        _ = UDPServerUtil.RestartUDPServerAsync(
            _settings.EnableConnectionFromOutside ? IPAddress.Any : IPAddress.Loopback,
            int.Parse(_settings.UDPPort),
            _wsjtxMsgHandler,
            _wsjtxMsgForwarder,
            _wsjtxMsgLogger
        );
    }

    private void _checkAndEnqueueQSO(RecordedCallsignDetail rcd)
    {
        if (rcd.IsUploadable())
        {
            if (_uploadQueue.Contains(rcd)) return;
            rcd.UploadStatus = UploadStatus.Pending;
            _uploadQueue.Enqueue(rcd);
            ClassLogger.Trace($"Enqueued QSO: {rcd}");
            return;
        }

        ClassLogger.Trace($"ignoring enqueue QSO: {rcd}");
    }

    private async Task _uploadCheckedQSO()
    {
        foreach (var recordedCallsignDetail in _allQsos.Items.Where(x => x.Checked))
        {
            recordedCallsignDetail.ForcedUpload = true;
            _checkAndEnqueueQSO(recordedCallsignDetail);
        }
    }

    private async Task _ignoreSelectedQSO()
    {
        var candidate = _allQsos.Items.Where(x => x.Checked).ToList();
        if (!candidate.Any())
        {
            await App.MessageBoxHelper.DoShowMessageboxAsync(new List<ButtonDefinition>()
            {
                new()
                {
                    Name = "OK",
                    IsDefault = true,
                }
            }, Icon.Info, "Notice", TranslationHelper.GetString("pseselfirst"));
            return;
        }
        var result = await App.MessageBoxHelper.DoShowMessageboxAsync(new List<ButtonDefinition>()
        {
            new()
            {
                Name = "OK",
            },
            new()
            {
                Name = "Cancel",
                IsDefault = true,
            }
        }, Icon.Warning, "Warning", TranslationHelper.GetString("ignoreqsopermanently"));
        if (result == "Cancel")return;
        foreach (var recordedCallsignDetail in candidate)
        {
            ClassLogger.Info($"Logging: {recordedCallsignDetail.ToString()}");
            await DatabaseUtil.MarkQsoIgnored(IgnoredQsoDatabase.Parse(recordedCallsignDetail));
            _allQsos.Edit(ls => ls.Remove(recordedCallsignDetail));
        } 
    }

    private async Task _createAdifFromCheckedQSO()
    {
        var currentVersion = VersionInfo.Version;
        var adif = new StringBuilder();
        adif.AppendLine("Cloudlog Helper ADIF Export");
        adif.AppendLine("<ADIF_VER:5>3.1.4");
        adif.AppendLine("<PROGRAMID:14>CloudlogHelper");
        adif.AppendLine($"<PROGRAMVERSION:{currentVersion.Length}>{currentVersion}");
        adif.AppendLine("<EOH>");
        adif.AppendLine();
        foreach (var recordedCallsignDetail in _allQsos.Items.Where(x => x.Checked))
        {
            adif.AppendLine(recordedCallsignDetail.GenerateAdif());
            adif.AppendLine();
        }

        // ask user to save 
        var file = await ShowFilePickerDialog.Handle(Unit.Default);
        if (file is null) return;

        var saveStream = await file.OpenWriteAsync();
        var st = new StreamWriter(saveStream);
        await st.WriteAsync(adif.ToString());
        st.Close();
    }


    /// <summary>
    ///     Note that cloudlog seems to process qsos single-thread...
    /// </summary>
    private async Task _uploadQSOFromQueue()
    {
        while (true)
            try
            {
                _isUploadQueueEmpty.OnNext(_uploadQueue.IsEmpty);
                if (!_uploadQueue.TryDequeue(out var rcd)) continue;
                var adif = rcd.RawData?.ToString()??rcd.GenerateAdif();
                if (string.IsNullOrEmpty(adif)) continue;
                ClassLogger.Trace($"Try Logging: {adif}");
                if (!_logServices.Any(x=>x.AutoQSOUploadEnabled) 
                    && !_extraCloudlogSettings.AutoQSOUploadEnabled 
                    && !rcd.ForcedUpload)
                {
                    rcd.UploadStatus = UploadStatus.Ignored;
                    rcd.FailReason = TranslationHelper.GetString("qsouploaddisabled");
                    ClassLogger.Debug($"Auto upload not enabled. ignored: {adif}.");
                    continue;
                }

                // do possible retry...
                if (!int.TryParse(_settings.RetryCount, out var retTime)) retTime = 1;
                for (var i = 0; i < retTime; i++)
                {
                    rcd.UploadStatus = i > 0 ? UploadStatus.Retrying : UploadStatus.Uploading;
                    rcd.FailReason = null;
                    var failOutput = new StringBuilder();

                    try
                    {
                        if (!_extraCloudlogSettings.AutoQSOUploadEnabled) rcd.UploadedServices["CloudlogService"] = true;
                        if (!rcd.UploadedServices.GetValueOrDefault("CloudlogService", false))
                        {
                            var cloudlogResult = await CloudlogUtil.UploadAdifLogAsync(
                                _extraCloudlogSettings.CloudlogUrl,
                                _extraCloudlogSettings.CloudlogApiKey,
                                _extraCloudlogSettings.CloudlogStationInfo?.StationId!, adif);
                            if (cloudlogResult.Status != "created")
                            {
                                ClassLogger.Debug("A qso for cloudlog failed to upload.");
                                rcd.UploadedServices["CloudlogService"] = false;
                                failOutput.AppendLine("Cloudlog: "+cloudlogResult.Reason.Trim());
                            }
                            else
                            {
                                ClassLogger.Debug("Qso for cloudlog uploaded successfully.");
                                rcd.UploadedServices["CloudlogService"] = true;
                            }
                        }

                        foreach (var thirdPartyLogService in _logServices)
                        {
                            var serName = thirdPartyLogService.GetType().Name;
                            if (!thirdPartyLogService.AutoQSOUploadEnabled) rcd.UploadedServices[serName] = true;
                            if (!rcd.UploadedServices.GetValueOrDefault(serName, false))
                            {
                                try
                                {
                                    await thirdPartyLogService.UploadQSOAsync(adif);
                                    rcd.UploadedServices[serName] = true;
                                }
                                catch (Exception ex)
                                {
                                    failOutput.AppendLine(serName + ex.Message);
                                }
                            }
                        }

                        if (rcd.UploadedServices.Values.Any(x => !x))
                        {
                            rcd.UploadStatus = UploadStatus.Success;
                            rcd.FailReason = string.Empty;
                            break;
                        }
                            
                        rcd.UploadStatus = UploadStatus.Fail;
                        rcd.FailReason = failOutput.ToString();

                        await Task.Delay(1000);
                    }
                    catch (Exception ex)
                    {
                        ClassLogger.Debug(ex, "Qso uploaded failed.");
                        rcd.UploadStatus = UploadStatus.Fail;
                        rcd.FailReason = ex.Message;
                    }
                }
            }
            catch (Exception st)
            {
                ClassLogger.Error(st, "Error occurred while uploading qso data. This is ignored.");
            }
            finally
            {
                await Task.Delay(500);
            }
    }

    private async void _wsjtxMsgForwarder(Memory<byte> message)
    {
        try
        {
            if (_settings.ForwardMessage)
                // ClassLogger.Trace(message.DeserializeWsjtxMessage().MessageType);
                await UDPServerUtil.ForwardMessageAsync(message, IPEndPoint.Parse(_settings.ForwardAddress));
        }
        catch (Exception e)
        {
            ClassLogger.Error(e, "Failed to process wsjtxmsg.");
        }
    }

    private async void _wsjtxMsgHandler(WsjtxMessage message)
    {
        try
        {
            switch (message.MessageType)
            {
                case MessageType.QSOLogged:
                    // No need to add semaphore; It is not async.
                    _allDecodedCount += 1;
                    _qsosCount += 1;
                    // process message
                    var msg = (QsoLogged)message;
                    var cty = await DatabaseUtil.GetCallsignDetailAsync(msg.DXCall);
                    var rcd = RecordedCallsignDetail.GenerateCallsignDetail(cty, msg);
                    rcd.ParentMode = await DatabaseUtil.GetParentModeAsync(rcd.Mode);
                    // log it into that
                    _allQsos.Add(rcd);
                    _checkAndEnqueueQSO(rcd);
                    break;
                case MessageType.Decode:
                    _allDecodedCount += 1;
                    break;
                case MessageType.Status:
                    var stat = (Status)message;
                    TxStatus = stat.Transmitting;
                    MsgSending = string.IsNullOrEmpty(stat.TXMessage)
                        ? TranslationHelper.GetString("txing")
                        : stat.TXMessage;
                    break;
            }

            QsosCountData = $"{_allDecodedCount}/{_qsosCount}";
            _heartbeatSubject.OnNext(Unit.Default);
            TimeoutStatus = false;
            WaitFirstConn = false;
        }
        catch (Exception e)
        {
            ClassLogger.Error(e, "Failed to process wsjtxmsg");
        }
    }

    private void _wsjtxMsgLogger(LogLevel level, string message)
    {
        if (level < LogLevel.Error) return;
        App.NotificationManager.SendWarningNotificationSync(message);
    }
}