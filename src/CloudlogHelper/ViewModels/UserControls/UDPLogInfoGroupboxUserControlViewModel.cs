using System;
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
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CloudlogHelper.Database;
using CloudlogHelper.Enums;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using DesktopNotifications;
using DynamicData;
using DynamicData.Binding;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using WsjtxUtilsPatch.WsjtxMessages.Messages;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using Notification = DesktopNotifications.Notification;

namespace CloudlogHelper.ViewModels.UserControls;

public class UDPLogInfoGroupboxUserControlViewModel : FloatableViewModelBase
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    /// <summary>
    ///     All qsologged message received.
    /// </summary>
    private readonly SourceList<RecordedCallsignDetail> _allQsos = new();

    private readonly IApplicationSettingsService _applicationSettingsService;

    private readonly IClipboardService _clipboardService;

    private readonly IDatabaseService _databaseService;

    private readonly IDecodedDataProcessorService _decodedDataProcessorService;


    /// <summary>
    ///     UDP Timeout watchdog.
    /// </summary>
    private readonly Subject<Unit> _heartbeatSubject = new();

    private readonly IInAppNotificationService _inAppNotification;

    private readonly IMessageBoxManagerService _messageBoxManagerService;

    private readonly INotificationManager _nativeNativeNotificationManager;

    private readonly IQSOUploadService _qsoUploadService;

    private readonly ReactiveCommand<Unit, Unit> _restartUdpCommand;

    private readonly IUdpServerService _udpServerService;

    /// <summary>
    ///     Settings for UDPServer.
    /// </summary>
    private readonly UDPServerSettings _udpSettings;

    /// <summary>
    ///     Total decoded number.
    /// </summary>
    private uint _allDecodedCount;

    /// <summary>
    ///     The number of Qso made.
    /// </summary>
    private uint _qsosCount;

    public UDPLogInfoGroupboxUserControlViewModel()
    {
        if (!Design.IsDesignMode) throw new InvalidOperationException("This should be called from designer only.");
        SelectAllCommand = ReactiveCommand.Create(() => { });
        ShowQSODetailCommand = ReactiveCommand.Create<RecordedCallsignDetail, Unit>(_ => Unit.Default);
        ReuploadSelectedCommand = ReactiveCommand.Create(() => { });
        ExportSelectedToAdiCommand = ReactiveCommand.Create(() => { });
        IgnoreSelectedPermanentlyCommand = ReactiveCommand.Create(() => { });
        DeleteSelectedCommand = ReactiveCommand.Create(() => { });
        _restartUdpCommand = ReactiveCommand.Create(() => { });
        ShowFilePickerDialog = new Interaction<Unit, IStorageFile?>();

        var testQso = new RecordedCallsignDetail
        {
            LocalizedCountryName = "日本",
            CqZone = 25,
            ItuZone = 45,
            Continent = "AS",
            Latitude = 35.6895f,
            Longitude = 139.6917f,
            GmtOffset = 9.0f,
            Dxcc = "JA",

            DateTimeOff = DateTime.Now,
            DateTimeOn = DateTime.Now.AddMinutes(-5),

            DXCall = "JA1ABC",
            DXGrid = "PM95",
            MyCall = "BA1ABC",
            MyGrid = "OM89",

            TXFrequencyInHz = 14074000,
            TXFrequencyInMeters = "10m",
            Mode = "FT4",
            ParentMode = "DIGITAL",

            ReportSent = "-07",
            ReportReceived = "+01",

            TXPower = "100W",
            Comments = "Nice contact with JA station",
            Name = "Taro",
            OperatorCall = "BH1ABC",

            ExchangeSent = "599",
            ExchangeReceived = "599",

            ClientId = "WSJT-X 50MHZ",
            UploadStatus = UploadStatus.Success,
            Checked = true,
            ForcedUpload = false,
            FailReason = null
        };

        for (var i = 0; i < 10; i++) FilteredQsos.Add(testQso);
    }

    public UDPLogInfoGroupboxUserControlViewModel(IDatabaseService dbService,
        IInAppNotificationService inAppNotification,
        IMessageBoxManagerService messageBoxManagerService,
        IUdpServerService udpServerService,
        IClipboardService clipboardService,
        IApplicationSettingsService ss,
        IQSOUploadService qu,
        INotificationManager nativeNotificationManager,
        IDecodedDataProcessorService decodedDataProcessorService)
    {
        _applicationSettingsService = ss;
        _clipboardService = clipboardService;
        _decodedDataProcessorService = decodedDataProcessorService;
        _nativeNativeNotificationManager = nativeNotificationManager;

        _qsoUploadService = qu;
        _udpSettings = ss.GetCurrentSettings().UDPSettings;

        _udpServerService = udpServerService;
        _databaseService = dbService;
        _messageBoxManagerService = messageBoxManagerService;
        _inAppNotification = inAppNotification;

        ShowFilePickerDialog = new Interaction<Unit, IStorageFile?>();
        WaitFirstConn = _udpSettings.EnableUDPServer;

        ShowQSODetailCommand = ReactiveCommand.CreateFromTask<RecordedCallsignDetail, Unit>(async callDet =>
        {
            var content = callDet.FormatToReadableContent();
            var msgResult = await _messageBoxManagerService.DoShowCustomMessageboxDialogAsync(
                new List<ButtonDefinition> { new() { Name = "OK" }, new() { Name = "Copy info" } },
                Icon.Info, "Detail", content);
            if (msgResult == "Copy info")
                // copy to clipboard
                await _clipboardService.SetTextAsync(content);
            return Unit.Default;
        });
        SelectAllCommand = ReactiveCommand.Create(() => { SelectAll = !SelectAll; });
        ReuploadSelectedCommand = ReactiveCommand.CreateFromTask(_uploadCheckedQSO);
        ExportSelectedToAdiCommand = ReactiveCommand.CreateFromTask(_createAdifFromCheckedQSO); //
        IgnoreSelectedPermanentlyCommand = ReactiveCommand.CreateFromTask(_ignoreSelectedQSO); //
        DeleteSelectedCommand = ReactiveCommand.Create(() =>
        {
            _allQsos.Edit(innerList =>
            {
                for (var i = innerList.Count - 1; i >= 0; i--)
                    if (innerList[i].Checked)
                        innerList.RemoveAt(i);
            });
        });
        _restartUdpCommand = ReactiveCommand.CreateFromTask(_restartUdp); //

        this.WhenActivated(disposables =>
        {
            // Our client watchdog
            _heartbeatSubject
                .Throttle(TimeSpan.FromSeconds(DefaultConfigs.UDPClientExpiryInSeconds))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    if (!_udpSettings.EnableUDPServer || WaitFirstConn) return;
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

            ShowQSODetailCommand.ThrownExceptions.Subscribe(async void (err) =>
                    await _inAppNotification.SendErrorNotificationAsync(err.Message))
                .DisposeWith(disposables);

            SelectAllCommand.ThrownExceptions.Subscribe(async void (err) =>
                    await _inAppNotification.SendErrorNotificationAsync(err.Message))
                .DisposeWith(disposables);

            ReuploadSelectedCommand.ThrownExceptions.Subscribe(async void (err) =>
                    await _inAppNotification.SendErrorNotificationAsync(err.Message))
                .DisposeWith(disposables);

            _restartUdpCommand.ThrownExceptions.Subscribe(async void (err) =>
                    await _inAppNotification.SendErrorNotificationAsync(err.Message))
                .DisposeWith(disposables);

            IgnoreSelectedPermanentlyCommand.ThrownExceptions.Subscribe(async void (err) =>
                    await _inAppNotification.SendErrorNotificationAsync(err.Message))
                .DisposeWith(disposables);

            ExportSelectedToAdiCommand.ThrownExceptions.Subscribe(async void (err) =>
                    await _inAppNotification.SendErrorNotificationAsync(err.Message))
                .DisposeWith(disposables);

            // refresh cloudlog infos immediately if settings changed.
            MessageBus.Current.Listen<SettingsChanged>()
                // .Where(x => x.Part == ChangedPart.UDPServer)
                .Subscribe(x =>
                {
                    if (x.Part == ChangedPart.UDPServer)
                    {
                        ClassLogger.Debug("Setting changed; updating udp");
                        WaitFirstConn = _udpSettings.EnableUDPServer;
                        if (ss.RestartUDPNeeded()) TryStartUdpService().DisposeWith(disposables);
                    }
                })
                .DisposeWith(disposables);

            MessageBus.Current.Listen<QsoUploadRequested>()
                .Subscribe(x =>
                {
                    foreach (var rcd in x.QsoData)
                    {
                        _allQsos.Add(rcd);
                        _qsoUploadService.EnqueueQSOForUpload(rcd);
                    }
                })
                .DisposeWith(disposables);


            TryStartUdpService().DisposeWith(disposables);
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
    public ReactiveCommand<RecordedCallsignDetail, Unit> ShowQSODetailCommand { get; set; }

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
        if (!_udpSettings.EnableUDPServer)
        {
            _ = _udpServerService.TerminateUDPServerAsync();
            return Disposable.Empty;
        }

        return Observable.Return(Unit.Default)
            .InvokeCommand(_restartUdpCommand);
    }

    private async Task _restartUdp()
    {
        ClassLogger.Debug("trying to start UDP...");
        // create a default handler here
        if (_udpSettings.IsUDPConfigHasErrors())
        {
            _ = _udpServerService.TerminateUDPServerAsync();
            WaitFirstConn = false;
            throw new Exception(TranslationHelper.GetString(LangKeys.invalidudpconf));
        }

        _ = _udpServerService.RestartUDPServerAsync(
            _udpSettings.EnableConnectionFromOutside ? IPAddress.Any : IPAddress.Loopback,
            int.Parse(_udpSettings.UDPPort),
            _wsjtxMsgHandler,
            _wsjtxMsgForwarder,
            _wsjtxMsgLogger
        );
    }

    private async Task _uploadCheckedQSO()
    {
        foreach (var recordedCallsignDetail in _allQsos.Items.Where(x => x.Checked))
        {
            recordedCallsignDetail.ForcedUpload = true;
            _qsoUploadService.EnqueueQSOForUpload(recordedCallsignDetail);
        }
    }

    private async Task _ignoreSelectedQSO()
    {
        var candidate = _allQsos.Items.Where(x => x.Checked).ToList();
        if (!candidate.Any())
        {
            await _messageBoxManagerService.DoShowCustomMessageboxDialogAsync(new List<ButtonDefinition>
            {
                new()
                {
                    Name = "OK",
                    IsDefault = true
                }
            }, Icon.Info, "Notice", TranslationHelper.GetString(LangKeys.pseselfirst));
            return;
        }

        var result = await _messageBoxManagerService.DoShowCustomMessageboxDialogAsync(new List<ButtonDefinition>
        {
            new()
            {
                Name = "OK"
            },
            new()
            {
                Name = "Cancel",
                IsDefault = true
            }
        }, Icon.Warning, "Warning", TranslationHelper.GetString(LangKeys.ignoreqsopermanently));
        if (result == "Cancel") return;
        foreach (var recordedCallsignDetail in candidate)
        {
            ClassLogger.Info($"Logging: {recordedCallsignDetail}");
            await _databaseService.MarkQsoIgnored(IgnoredQsoDatabase.Parse(recordedCallsignDetail));
            _allQsos.Edit(ls => ls.Remove(recordedCallsignDetail));
        }
    }

    private async Task _createAdifFromCheckedQSO()
    {
        var currentVersion = VersionInfo.Version;
        var adif = new StringBuilder(AdifUtil.GenerateHeader());
        foreach (var recordedCallsignDetail in _allQsos.Items.Where(x => x.Checked))
            adif.AppendLine(recordedCallsignDetail.GenerateAdif());

        // ask user to save 
        var file = await ShowFilePickerDialog.Handle(Unit.Default);
        if (file is null) return;

        var saveStream = await file.OpenWriteAsync();
        var st = new StreamWriter(saveStream);
        await st.WriteAsync(adif.ToString());
        st.Close();
    }

    private async void _wsjtxMsgForwarder(Memory<byte> message)
    {
        try
        {
            if (_udpSettings.ForwardMessage)
                // ClassLogger.Trace(message.DeserializeWsjtxMessage().MessageType);
                await _udpServerService.ForwardUDPMessageAsync(message, IPEndPoint.Parse(_udpSettings.ForwardAddress));

            if (_udpSettings.ForwardMessageToHttp)
                await _udpServerService.ForwardTCPMessageAsync(message, _udpSettings.ForwardHttpAddress);
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
                    var cty = await _databaseService.GetCallsignDetailAsync(msg.DXCall);
                    var rcd = RecordedCallsignDetail.GenerateCallsignDetail(cty, msg,
                        _applicationSettingsService.GetCurrentSettings().BasicSettings.LanguageType);
                    rcd.ParentMode = await _databaseService.GetParentModeAsync(rcd.Mode);

                    // log it into that
                    _allQsos.Add(rcd);
                    _qsoUploadService.EnqueueQSOForUpload(rcd);

                    // notify users
                    if (_udpSettings.PushNotificationOnQSOMade)
                        _ = _nativeNativeNotificationManager.ShowNotification(new Notification
                        {
                            Title = TranslationHelper.GetString(LangKeys.madeaqso) + rcd.DXCall,
                            Body = rcd.FormatToReadableContent(true)
                        });
                    break;
                case MessageType.Decode:
                    _allDecodedCount += 1;
                    var decMsg = (Decode)message;
                    _decodedDataProcessorService.ProcessDecoded(decMsg);
                    break;
                case MessageType.Status:
                    var stat = (Status)message;
                    TxStatus = stat.Transmitting;
                    MsgSending = string.IsNullOrEmpty(stat.TXMessage)
                        ? TranslationHelper.GetString(LangKeys.txing)
                        : stat.TXMessage;
                    _decodedDataProcessorService.UpdateClientBand(stat.Id,
                        FreqHelper.GetMeterFromFreq(stat.DialFrequencyInHz));
                    MessageBus.Current.SendMessage(new ClientStatusChanged
                    {
                        CurrStatus = stat
                    });
                    ClassLogger.Trace(
                        $"Status changed: {stat.DialFrequencyInHz} {stat.ConfigurationName} {""} {stat.Mode}");
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
        _inAppNotification.SendWarningNotificationSync(message);
    }
}