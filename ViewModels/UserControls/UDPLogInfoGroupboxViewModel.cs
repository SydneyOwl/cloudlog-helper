using System;
using System.Collections.Concurrent;
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
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using DynamicData;
using DynamicData.Binding;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using WsjtxUtils.WsjtxMessages.Messages;
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
    ///     UDP Timeout watchdog.
    /// </summary>
    private readonly Subject<Unit> _heartbeatSubject = new();

    /// <summary>
    ///     check if this queue is empty. Reupload qso function is disabled if queue is not empty.
    /// </summary>
    private readonly BehaviorSubject<bool> _isUploadQueueEmpty;

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
    ///     Settings for cloudlog.
    /// </summary>
    private CloudlogSettings _extraCloudlogSettings = ApplicationSettings.GetInstance().CloudlogSettings.DeepClone();

    /// <summary>
    ///     Settings for clublog.
    /// </summary>
    private ClublogSettings _extraClublogSettings = ApplicationSettings.GetInstance().ClublogSettings.DeepClone();

    /// <summary>
    ///     The number of Qso made.
    /// </summary>
    private uint _qsosCount;


    /// <summary>
    ///     Settings for UDPServer.
    /// </summary>
    private UDPServerSettings _settings = ApplicationSettings.GetInstance().UDPSettings.DeepClone();

    public UDPLogInfoGroupboxViewModel()
    {
        _isUploadQueueEmpty = new BehaviorSubject<bool>(_uploadQueue.IsEmpty);
        ShowFilePickerDialog = new Interaction<Unit, IStorageFile?>();
        WaitFirstConn = _settings.EnableUDPServer;

        SelectAllCommand = ReactiveCommand.Create(() => { SelectAll = !SelectAll; });
        ReuploadSelectedCommand = ReactiveCommand.CreateFromTask(_uploadCheckedQSO, _isUploadQueueEmpty.AsObservable());
        ExportSelectedToAdiCommand = ReactiveCommand.CreateFromTask(_createAdifFromCheckedQSO);
        DeleteSelectedCommand = ReactiveCommand.Create(() =>
        {
            var items = _allQsos.Items.ToList();
            _allQsos.Clear();
            items = items.Where(x => !x.Checked).ToList();
            _allQsos.AddRange(items);
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

            // Call filter
            _allQsos.Connect()
                .Transform(x =>
                {
                    if (ShowFailedOnly)
                        if (x.UploadStatus == UploadStatus.Success)
                        {
                            x.Checked = false;
                            return x;
                        }

                    x.Checked = SelectAll;
                    return x;
                })
                .Filter(detail => (detail.UploadStatus != UploadStatus.Success && ShowFailedOnly) || !ShowFailedOnly)
                .Bind(FilteredQsos)
                .Subscribe()
                .DisposeWith(disposables);

            // Callsign update trigger
            this.WhenAnyValue(x => x.SelectAll, x => x.ShowFailedOnly)
                .Subscribe(res =>
                {
                    var items = _allQsos.Items.ToList();
                    _allQsos.Clear();
                    _allQsos.AddRange(items);
                }).DisposeWith(disposables);

            RestartUdpCommand.ThrownExceptions.Subscribe(err => { SendMsgToParentVm(err.Message); })
                .DisposeWith(disposables);

            UploadLogFromQueueCommand.ThrownExceptions.Subscribe(err => { SendMsgToParentVm(err.Message); })
                .DisposeWith(disposables);

            ExportSelectedToAdiCommand.ThrownExceptions.Subscribe(err => { SendMsgToParentVm(err.Message); })
                .DisposeWith(disposables);

            // refresh cloudlog infos immediately if settings changed.
            MessageBus.Current.Listen<SettingsChanged>()
                // .Where(x => x.Part == ChangedPart.UDPServer)
                .Subscribe(x =>
                {
                    if (x.Part == ChangedPart.UDPServer)
                    {
                        ClassLogger.Debug("Setting changed; updating udp");
                        // // update settings cache
                        _settings = ApplicationSettings.GetInstance().UDPSettings.DeepClone();
                        WaitFirstConn = _settings.EnableUDPServer;
                        TryStartUdpService().DisposeWith(disposables);
                    }

                    if (x.Part == ChangedPart.Cloudlog)
                        _extraCloudlogSettings = ApplicationSettings.GetInstance().CloudlogSettings.DeepClone();

                    if (x.Part == ChangedPart.Clublog)
                        _extraClublogSettings = ApplicationSettings.GetInstance().ClublogSettings.DeepClone();
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
            SendMsgToParentVm("");
            return Disposable.Empty;
        }

        return Observable.Return(Unit.Default)
            .InvokeCommand(RestartUdpCommand);
    }

    private async Task _restartUdp()
    {
        ClassLogger.Debug("trying to start UDP...");
        await Task.Delay(500); //dirty... Validation part in Settings(init) is not ready yet so wait for 500ms
        // create a default handler here
        if (_settings.IsUDPConfigHasErrors())
        {
            UDPServerUtil.TerminateUDPServer();
            WaitFirstConn = false;
            throw new Exception(TranslationHelper.GetString("invalidudpconf"));
        }

        SendMsgToParentVm("");

        _ = UDPServerUtil.RestartUDPServerAsync(
            _settings.EnableConnectionFromOutside ? IPAddress.Any : IPAddress.Loopback,
            int.Parse(_settings.UDPPort),
            _wsjtxMsgHandler,
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
            adif.AppendLine(_generateAdifFromRecordedCallsignDetail(recordedCallsignDetail));
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
        {
            _isUploadQueueEmpty.OnNext(_uploadQueue.IsEmpty);
            if (_uploadQueue.TryDequeue(out var rcd))
            {
                var adif = _generateAdifFromRecordedCallsignDetail(rcd);
                ClassLogger.Debug($"Try Logging: {adif}");
                if (_settings is { AutoUploadQSOToCloudlog: false, AutoUploadQSOToClublog: false } && !rcd.ForcedUpload)
                {
                    rcd.UploadStatus = UploadStatus.Ignored;
                    rcd.FailReason = TranslationHelper.GetString("qsouploaddisabled");
                    ClassLogger.Debug($"Auto upload to cloudlog and clublog not enabled. ignored: {adif}.");
                    continue;
                }

                // do possible retry...
                var cloudlogUploaded = false;
                var clublogUploaded = false;
                for (var i = 0; i < _settings.RetryCount.Length; i++)
                {
                    rcd.UploadStatus = i > 0 ? UploadStatus.Retrying : UploadStatus.Uploading;
                    rcd.FailReason = null;
                    var failOutput = new StringBuilder();

                    try
                    {
                        if (!_settings.AutoUploadQSOToCloudlog) cloudlogUploaded = true;
                        if (!cloudlogUploaded)
                        {
                            var cloudlogResult = await CloudlogUtil.UploadAdifLogAsync(_extraCloudlogSettings.CloudlogUrl,
                                _extraCloudlogSettings.CloudlogApiKey,
                                _extraCloudlogSettings.CloudlogStationId, adif);
                            if (cloudlogResult.Status != "created")
                            {
                                ClassLogger.Debug("A qso for cloudlog failed to upload.");
                                cloudlogUploaded = false;
                                failOutput.AppendLine(cloudlogResult.Reason);
                            }
                            else
                            {
                                ClassLogger.Debug("Qso for cloudlog uploaded successfully.");
                                cloudlogUploaded = true;
                            }
                        }

                        if (!_settings.AutoUploadQSOToClublog) clublogUploaded = true;
                        if (!clublogUploaded)
                        {
                            var clublogResult = await ClublogUtil.UploadQSOToClublogAsync(
                                _extraClublogSettings.ClublogCallsign, _extraClublogSettings.ClublogPassword,
                                _extraClublogSettings.ClublogEmail, adif);

                            if (string.IsNullOrEmpty(clublogResult))
                            {
                                // success
                                ClassLogger.Debug("A qso for clublog uploaded succcessfully.");
                                clublogUploaded = true;
                            }
                            else
                            {
                                ClassLogger.Debug("A qso for clublog failed to upload.");
                                clublogUploaded = false;
                                failOutput.AppendLine(clublogResult);
                            }
                        }
                        
                        if (clublogUploaded && cloudlogUploaded)
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
                        ClassLogger.Debug($"Qso uploaded failed:{ex.Message}.");
                        rcd.UploadStatus = UploadStatus.Fail;
                        rcd.FailReason = ex.Message;
                    }
                }
            }

            await Task.Delay(500);
        }
    }

    private string _generateAdifFromRecordedCallsignDetail(RecordedCallsignDetail rcd)
    {
        var adif = AdifUtil.GenerateAdifLog(new AdifLog
        {
            Call = rcd.DXCall,
            GridSquare = rcd.DXGrid,
            Mode = string.IsNullOrEmpty(rcd.ParentMode) ? rcd.Mode : rcd.ParentMode,
            SubMode = string.IsNullOrEmpty(rcd.ParentMode) ? string.Empty : rcd.Mode,
            RstSent = rcd.ReportSent,
            RstRcvd = rcd.ReportReceived,
            QsoDate = rcd.DateTimeOn.ToString("yyyyMMdd"),
            TimeOn = rcd.DateTimeOn.ToString("HHmmss"),
            QsoDateOff = rcd.DateTimeOff.ToString("yyyyMMdd"),
            TimeOff = rcd.DateTimeOff.ToString("HHmmss"),
            Band = rcd.TXFrequencyInMeters,
            Freq = (rcd.TXFrequencyInHz / 1_000_000.0).ToString("0.000000"),
            StationCallsign = rcd.MyCall,
            MyGridSquare = rcd.MyGrid,
            Comment = rcd.Comments
        });
        return adif;
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
            ClassLogger.Error($"Failed to process wsjtxmsg: {e.Message}");
        }
    }

    private void _wsjtxMsgLogger(LogLevel level, string message)
    {
        if (level < LogLevel.Error) return;
        SendMsgToParentVm(message);
    }
}