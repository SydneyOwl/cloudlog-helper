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
using System.Threading;
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
using SpecialOperationMode = CloudlogHelper.CLHProto.SpecialOperationMode;

namespace CloudlogHelper.ViewModels.UserControls;

public class UDPLogInfoGroupboxUserControlViewModel : FloatableViewModelBase
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private readonly SourceList<RecordedCallsignDetail> _allQsos = new();
    private readonly IApplicationSettingsService _applicationSettingsService;
    private readonly IDatabaseService _databaseService;
    private readonly IDecodedDataProcessorService _decodedDataProcessorService;
    private readonly Subject<Unit> _heartbeatSubject = new();
    private readonly IInAppNotificationService _inAppNotification;
    private readonly IMessageBoxManagerService _messageBoxManagerService;
    private readonly INotificationManager _nativeNotificationManager;
    private readonly IQSOUploadService _qsoUploadService;
    private readonly IUdpServerService _udpServerService;
    private readonly ICLHServerService _clhServerService;

    private readonly ConcurrentQueue<DateTime> _qsoTimestamps = new();

    public UDPLogInfoGroupboxUserControlViewModel()
    {
        if (!Design.IsDesignMode) throw new InvalidOperationException("This should be called from designer only.");
        SelectAllCommand = ReactiveCommand.Create(() => { });
        ShowQSODetailCommand = ReactiveCommand.Create<RecordedCallsignDetail, Unit>(_ => Unit.Default);
        ReuploadSelectedCommand = ReactiveCommand.Create(() => { });
        ExportSelectedToAdiCommand = ReactiveCommand.Create(() => { });
        IgnoreSelectedPermanentlyCommand = ReactiveCommand.Create(() => { });
        DeleteSelectedCommand = ReactiveCommand.Create(() => { });
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

        // TxStatus = true;
        // MsgSending = "Hello sending...";

        // TimeoutStatus = true;

        // WaitFirstConn = true;
    }

    public UDPLogInfoGroupboxUserControlViewModel(
        IDatabaseService dbService,
        IInAppNotificationService inAppNotification,
        IMessageBoxManagerService messageBoxManagerService,
        IUdpServerService udpServerService,
        IClipboardService clipboardService,
        IApplicationSettingsService ss,
        IQSOUploadService qu,
        INotificationManager nativeNotificationManager,
        IDecodedDataProcessorService decodedDataProcessorService,
        ICLHServerService clhServerService)
    {
        _applicationSettingsService = ss;
        var clipboardService1 = clipboardService;
        _decodedDataProcessorService = decodedDataProcessorService;
        _nativeNotificationManager = nativeNotificationManager;
        _qsoUploadService = qu;
        _udpServerService = udpServerService;
        _databaseService = dbService;
        _messageBoxManagerService = messageBoxManagerService;
        _inAppNotification = inAppNotification;

        _clhServerService = clhServerService;

        ShowFilePickerDialog = new Interaction<Unit, IStorageFile?>();
        WaitFirstConn = _udpServerService.IsUdpServerEnabled();

        ShowQSODetailCommand = ReactiveCommand.CreateFromTask<RecordedCallsignDetail, Unit>(async callDet =>
        {
            var content = callDet.FormatToReadableContent();
            var msgResult = await _messageBoxManagerService.DoShowCustomMessageboxDialogAsync(
                new List<ButtonDefinition> 
                { 
                    new() { Name = "OK" }, 
                    new() { Name = "Copy info" } 
                },
                Icon.Info, 
                "Detail", 
                content);
                
            if (msgResult == "Copy info")
                await clipboardService1.SetTextAsync(content);
                
            return Unit.Default;
        });
        
        SelectAllCommand = ReactiveCommand.Create(() => { SelectAll = !SelectAll; });
        ReuploadSelectedCommand = ReactiveCommand.CreateFromTask(_uploadCheckedQSO);
        ExportSelectedToAdiCommand = ReactiveCommand.CreateFromTask(_createAdifFromCheckedQSO);
        IgnoreSelectedPermanentlyCommand = ReactiveCommand.CreateFromTask(_ignoreSelectedQSO);
        DeleteSelectedCommand = ReactiveCommand.Create(_deleteSelectedQsos);

        this.WhenActivated(disposables =>
        {
            var compositeDisposable = new CompositeDisposable();
            disposables.Add(compositeDisposable);

            _heartbeatSubject
                .Throttle(TimeSpan.FromSeconds(DefaultConfigs.UDPClientExpiryInSeconds))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    if (!_udpServerService.IsUdpServerEnabled() || WaitFirstConn) return;
                    TimeoutStatus = true;
                    TxStatus = false;
                })
                .DisposeWith(compositeDisposable);

            SetupQsoRateCalculation(compositeDisposable);
            SetupFilteringAndSorting(compositeDisposable);

            this.WhenAnyValue(
                    x => x.SelectAll, 
                    x => x.ShowFailedOnly)
                .Throttle(TimeSpan.FromMilliseconds(50))
                .Subscribe(res => UpdateSelectionState())
                .DisposeWith(compositeDisposable);

            SetupErrorHandling(compositeDisposable);
            SetupMessageBusListeners(compositeDisposable);

            _ = _udpServerService.InitializeAsync(_wsjtxMsgHandler, _wsjtxMsgLogger);
        });
    }

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
    [Reactive] public string MsgSending { get; set; } = string.Empty;
    [Reactive] public string? QsAvgMin { get; set; } = "0 Q's/m";
    
    // seems like it's not necessary,,,
    [Reactive] public long AllDecodedCount { get; set; }
    [Reactive] public long RecordedQsosCount { get; set; }
    [Reactive] public long UploadedQsosCount { get; set; }
    [Reactive] public bool SelectAll { get; set; }
    [Reactive] public bool ShowFailedOnly { get; set; }

    private void SetupQsoRateCalculation(CompositeDisposable disposables)
    {
        Observable.Interval(TimeSpan.FromSeconds(10))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                var cutoffTime = DateTime.UtcNow.AddMinutes(-5);
                while (_qsoTimestamps.TryPeek(out var timestamp) 
                       && timestamp < cutoffTime)
                {
                    _qsoTimestamps.TryDequeue(out var _);
                }

                var rate = _qsoTimestamps.Count > 0 
                    ? _qsoTimestamps.Count / 5.0
                    : 0;
                    
                QsAvgMin = $"{rate:F2} Q's/m";
            }).DisposeWith(disposables);
            

        _allQsos.Connect()
            .WhenPropertyChanged(p => p.UploadStatus)
            .Where(x => x.Value == UploadStatus.Success)
            .Subscribe(x =>
            {
                UploadedQsosCount += 1;
            })
            .DisposeWith(disposables);
    }

    private void SetupFilteringAndSorting(CompositeDisposable disposables)
    {
        var filterObservable = this.WhenAnyValue(x => x.ShowFailedOnly)
            .Throttle(TimeSpan.FromMilliseconds(200))
            .Select(showFailed => (Func<RecordedCallsignDetail, bool>)(detail =>
                !showFailed || detail.UploadStatus != UploadStatus.Success));

        _allQsos.Connect()
            .Filter(filterObservable)
            .Sort(SortExpressionComparer<RecordedCallsignDetail>
                .Ascending(x => x.DateTimeOff))
            .Bind(FilteredQsos)
            .Subscribe()
            .DisposeWith(disposables);
    }

    private void SetupErrorHandling(CompositeDisposable disposables)
    {
        var commands = new IReactiveCommand[]
        {
            ShowQSODetailCommand,
            SelectAllCommand,
            ReuploadSelectedCommand,
            IgnoreSelectedPermanentlyCommand,
            ExportSelectedToAdiCommand
        };

        foreach (var command in commands)
        {
            command.ThrownExceptions
                .Subscribe(async err => 
                    await _inAppNotification.SendErrorNotificationAsync(err.Message))
                .DisposeWith(disposables);
        }
    }

    private void SetupMessageBusListeners(CompositeDisposable disposables)
    {
        MessageBus.Current.Listen<SettingsChanged>()
            .Where(x => x.Part == ChangedPart.UDPServer)
            .Throttle(TimeSpan.FromMilliseconds(100)) // 防抖
            .Subscribe(_ =>
            {
                WaitFirstConn = _udpServerService.IsUdpServerEnabled();
            })
            .DisposeWith(disposables);

        MessageBus.Current.Listen<QsoUploadRequested>()
            .Subscribe(x =>
            {
                _allQsos.Edit(innerList =>
                {
                    innerList.AddRange(x.QsoData);
                });
                
                foreach (var rcd in x.QsoData)
                {
                    _qsoUploadService.EnqueueQSOForUpload(rcd);
                }
            })
            .DisposeWith(disposables);
    }

    private void UpdateSelectionState()
    {
        _allQsos.Edit(innerList =>
        {
            foreach (var item in innerList)
            {
                if (ShowFailedOnly && item.UploadStatus == UploadStatus.Success)
                {
                    item.Checked = false;
                    continue;
                }
                item.Checked = SelectAll;
            }
        });
    }

    private void _deleteSelectedQsos()
    {
        var itemsToRemove = _allQsos.Items
            .Where(x => x.Checked)
            .ToList();
            
        if (itemsToRemove.Count == 0) return;
            
        _allQsos.Edit(innerList =>
        {
            foreach (var item in itemsToRemove)
            {
                innerList.Remove(item);
            }
        });
    }
    

    private async Task _uploadCheckedQSO()
    {
        var itemsToUpload = _allQsos.Items
            .Where(x => x.Checked && x.UploadStatus != UploadStatus.Success)
            .ToList();
            
        foreach (var item in itemsToUpload)
        {
            item.ForcedUpload = true;
            _qsoUploadService.EnqueueQSOForUpload(item);
        }
    }

    private async Task _ignoreSelectedQSO()
    {
        var candidates = _allQsos.Items
            .Where(x => x.Checked)
            .ToList();
            
        if (candidates.Count == 0)
        {
            await _messageBoxManagerService.DoShowCustomMessageboxDialogAsync(
                new List<ButtonDefinition> { new() { Name = "OK", IsDefault = true } },
                Icon.Info, 
                "Notice", 
                TranslationHelper.GetString(LangKeys.pseselfirst));
            return;
        }

        var result = await _messageBoxManagerService.DoShowCustomMessageboxDialogAsync(
            new List<ButtonDefinition>
            {
                new() { Name = "OK" },
                new() { Name = "Cancel", IsDefault = true }
            },
            Icon.Warning, 
            "Warning", 
            TranslationHelper.GetString(LangKeys.ignoreqsopermanently));
            
        if (result == "Cancel") return;
        
        foreach (var item in candidates)
        {
            ClassLogger.Info($"Ignoring QSO: {item.DXCall}");
            await _databaseService.MarkQsoIgnored(IgnoredQsoDatabase.Parse(item));
            _allQsos.Remove(item);
        }
    }

    private async Task _createAdifFromCheckedQSO()
    {
        var checkedItems = _allQsos.Items
            .Where(x => x.Checked)
            .ToList();
            
        if (checkedItems.Count == 0) return;
            
        var adif = new StringBuilder(AdifUtil.GenerateHeader());
        foreach (var item in checkedItems)
        {
            adif.AppendLine(item.GenerateAdif());
        }

        var file = await ShowFilePickerDialog.Handle(Unit.Default);
        if (file is null) return;

        await using var saveStream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(saveStream);
        await writer.WriteAsync(adif.ToString());
    }

    private async void _wsjtxMsgHandler(WsjtxMessage message)
    {
        try
        {
            switch (message.MessageType)
            {
                case MessageType.QSOLogged:
                    AllDecodedCount += 1;
                    await HandleQsoLogged((QsoLogged)message);
                    break;
                    
                case MessageType.Decode:
                    AllDecodedCount += 1;
                    await HandleDecode((Decode)message);
                    break;
                    
                case MessageType.Status:
                    await HandleStatusMessage((Status)message);
                    break;
                
                case MessageType.WSPRDecode:
                    AllDecodedCount += 1;
                    await HandleWSPRDecode((WSPRDecode)message);
                    break;
            }

            _heartbeatSubject.OnNext(Unit.Default);
            TimeoutStatus = false;
            WaitFirstConn = false;
        }
        catch (Exception e)
        {
            await _inAppNotification.SendErrorNotificationAsync("Failed to process QSO: " + e.Message);
            ClassLogger.Error(e, "Failed to process WSJT-X message");
        }
    }

    private async Task HandleQsoLogged(QsoLogged message)
    {
        RecordedQsosCount += 1;
        _qsoTimestamps.Enqueue(DateTime.UtcNow);

        var cty = await _databaseService.GetCallsignDetailAsync(message.DXCall);
        var rcd = RecordedCallsignDetail.GenerateCallsignDetail(
            cty, 
            message,
            _applicationSettingsService.GetCurrentSettings().BasicSettings.LanguageType);
            
        rcd.ParentMode = await _databaseService.GetParentModeAsync(rcd.Mode);

        _allQsos.Add(rcd);
        _qsoUploadService.EnqueueQSOForUpload(rcd);

        if (_udpServerService.IsNotifyOnQsoMade())
        {
            _ = _nativeNotificationManager.ShowNotification(new Notification
            {
                Title = TranslationHelper.GetString(LangKeys.madeaqso) + rcd.DXCall,
                Body = rcd.FormatToReadableContent(true)
            });
        }
    }


    private async Task HandleDecode(Decode message)
    {
        await _clhServerService.SendDataNoException(new CLHProto.Decode
        {
            New = message.New,
            Time = message.Time,
            Snr = message.Snr,
            OffsetTimeSeconds = message.OffsetTimeSeconds,
            OffsetFrequencyHz = message.OffsetFrequencyHz,
            Mode = message.Mode,
            Message = message.Message,
            LowConfidence = message.LowConfidence,
            OffAir = message.OffAir,
        });
        _decodedDataProcessorService.ProcessDecoded(message);
    }
    
    private async Task HandleWSPRDecode(WSPRDecode message)
    {
        await _clhServerService.SendDataNoException(new CLHProto.WSPRDecode
        {
            New = message.New,
            Time = message.Time,
            Snr = message.Snr,
            DeltaTimeSeconds = message.DeltaTimeSeconds,
            FrequencyHz = message.FrequencyHz,
            FrequencyDriftHz = message.FrequencyDriftHz,
            Callsign = message.Callsign,
            Grid = message.Grid,
            Power = (uint)message.Power,
            OffAir = message.OffAir
        });
    }

    private async Task HandleStatusMessage(Status status)
    {
        TxStatus = status.Transmitting;
        MsgSending = string.IsNullOrEmpty(status.TXMessage)
            ? TranslationHelper.GetString(LangKeys.txing)
            : status.TXMessage;
            
        _decodedDataProcessorService.UpdateClientBand(
            status.Id,
            FreqHelper.GetMeterFromFreq(status.DialFrequencyInHz));
            
        MessageBus.Current.SendMessage(new ClientStatusChanged
        {
            CurrStatus = status
        });

        var som = status.SpecialOperationMode switch
        {
            WsjtxUtilsPatch.WsjtxMessages.Messages.SpecialOperationMode.NONE => SpecialOperationMode.None,
            WsjtxUtilsPatch.WsjtxMessages.Messages.SpecialOperationMode.NAVHF => SpecialOperationMode.Navhf,
            WsjtxUtilsPatch.WsjtxMessages.Messages.SpecialOperationMode.EUVHF => SpecialOperationMode.Euvhf,
            WsjtxUtilsPatch.WsjtxMessages.Messages.SpecialOperationMode.FIELDDAY => SpecialOperationMode.Fieldday,
            WsjtxUtilsPatch.WsjtxMessages.Messages.SpecialOperationMode.RTTYRU => SpecialOperationMode.Rttyru,
            WsjtxUtilsPatch.WsjtxMessages.Messages.SpecialOperationMode.FOX => SpecialOperationMode.Fox,
            WsjtxUtilsPatch.WsjtxMessages.Messages.SpecialOperationMode.HOUND => SpecialOperationMode.Hound,
            _ => SpecialOperationMode.None
        };


        await _clhServerService.SendDataNoException(new CLHProto.Status
        {
            DialFrequencyHz = status.DialFrequencyInHz,
            Mode = status.Mode,
            DxCall = status.DXCall,
            Report = status.Report,
            TxMode = status.TXMode,
            TxEnabled = status.TXEnabled,
            Transmitting = status.Transmitting,
            Decoding = status.Decoding,
            RxOffsetFrequencyHz = status.RXOffsetFrequencyHz,
            TxOffsetFrequencyHz = status.TXOffsetFrequencyHz,
            DeCall = status.DECall,
            DeGrid = status.DEGrid,
            DxGrid = status.DXGrid,
            TxWatchdog = status.TXWatchdog,
            SubMode = status.SubMode,
            FastMode = status.FastMode,
            SpecialOperationMode = som,
            FrequencyTolerance = status.FrequencyTolerance,
            TrPeriod = status.TRPeriod,
            ConfigurationName = status.ConfigurationName,
            TxMessage = status.TXMessage,
        });
        
        ClassLogger.Trace(
            $"Status changed: {status.DialFrequencyInHz} {status.ConfigurationName} {status.Mode}");
    }

    private void _wsjtxMsgLogger(LogLevel level, string message)
    {
        if (level < LogLevel.Error) return;
        _inAppNotification.SendWarningNotificationSync(message);
    }
}