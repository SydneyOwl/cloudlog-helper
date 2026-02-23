using System;
using System.Drawing;
using CloudlogHelper.Models;
using Google.Protobuf.WellKnownTypes;
using SydneyOwl.CLHProto.Plugin;
using Color = Avalonia.Media.Color;

namespace CloudlogHelper.Utils;

public class PbMsgConverter
{
    public static RigData ToPbRigData(string provider, RadioData allInfo)
    {
        // parse to pb
        var pbRig = new RigData
        {
            Provider = provider,
            RigName = allInfo.RigName,
            Frequency = (ulong)allInfo.FrequencyTx,
            Mode = allInfo.ModeTx,
            FrequencyRx = (ulong)allInfo.FrequencyRx,
            ModeRx = allInfo.ModeRx,
            Split = allInfo.IsSplit,
            Power = (uint)(allInfo.Power ?? 0),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
        };
        return pbRig;
    }

    public static WsjtxMessage? ToPbWsjtxMessage(WsjtxUtilsPatch.WsjtxMessages.Messages.WsjtxMessage msg)
    {
        var pbMsg = new WsjtxMessage
        {
            Header = new MessageHeader
            {
                MagicNumber = msg.MagicNumber,
                SchemaNumber = (uint)msg.SchemaVersion,
                Type = MapMessageType(msg.MessageType),
                Id = msg.Id ?? string.Empty
            },
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        switch (msg)
        {
            case WsjtxUtilsPatch.WsjtxMessages.Messages.Heartbeat hb:
                pbMsg.Heartbeat = new Heartbeat
                {
                    MaxSchemaNumber = (uint)hb.MaximumSchemaNumber,
                    Version = hb.Version ?? string.Empty,
                    Revision = hb.Revision ?? string.Empty
                };
                break;
            case WsjtxUtilsPatch.WsjtxMessages.Messages.Status status:
                pbMsg.Status = new Status
                {
                    DialFrequency = status.DialFrequencyInHz,
                    Mode = status.Mode ?? string.Empty,
                    DxCall = status.DXCall ?? string.Empty,
                    Report = status.Report ?? string.Empty,
                    TxMode = status.TXMode ?? string.Empty,
                    TxEnabled = status.TXEnabled,
                    Transmitting = status.Transmitting,
                    Decoding = status.Decoding,
                    RxDf = status.RXOffsetFrequencyHz,
                    TxDf = status.TXOffsetFrequencyHz,
                    DeCall = status.DECall ?? string.Empty,
                    DeGrid = status.DEGrid ?? string.Empty,
                    DxGrid = status.DXGrid ?? string.Empty,
                    TxWatchdog = status.TXWatchdog,
                    SubMode = status.SubMode ?? string.Empty,
                    FastMode = status.FastMode,
                    SpecialOpMode = MapSpecialOperationMode(status.SpecialOperationMode),
                    FrequencyTolerance = status.FrequencyTolerance,
                    TrPeriod = status.TRPeriod,
                    ConfigName = status.ConfigurationName ?? string.Empty,
                    TxMessage = status.TXMessage ?? string.Empty
                };
                break;
            case WsjtxUtilsPatch.WsjtxMessages.Messages.Decode decode:
                pbMsg.Decode = new Decode
                {
                    IsNew = decode.New,
                    Time = Timestamp.FromDateTime(DateTime.UtcNow.AddMilliseconds(decode.Time)),
                    Snr = decode.Snr,
                    DeltaTime = decode.OffsetTimeSeconds,
                    DeltaFrequency = decode.OffsetFrequencyHz,
                    Mode = decode.Mode ?? string.Empty,
                    Message = decode.Message ?? string.Empty,
                    LowConfidence = decode.LowConfidence,
                    OffAir = decode.OffAir
                };
                break;
            case WsjtxUtilsPatch.WsjtxMessages.Messages.Clear clear:
                pbMsg.Clear = new Clear
                {
                    Window = MapClearWindow(clear.Window)
                };
                break;
            case WsjtxUtilsPatch.WsjtxMessages.Messages.Reply reply:
                pbMsg.Reply = new Reply
                {
                    Time = Timestamp.FromDateTime(DateTime.UtcNow.AddMilliseconds(reply.Time)),
                    Snr = reply.Snr,
                    DeltaTime = reply.OffsetTimeSeconds,
                    DeltaFrequency = reply.OffsetFrequencyHz,
                    Mode = reply.Mode ?? string.Empty,
                    Message = reply.Message ?? string.Empty,
                    LowConfidence = reply.LowConfidence,
                    Modifiers = (uint)reply.Modifiers
                };
                break;
            case WsjtxUtilsPatch.WsjtxMessages.Messages.QsoLogged qsoLogged:
                pbMsg.QsoLogged = new QSOLogged
                {
                    DatetimeOff = Timestamp.FromDateTime(qsoLogged.DateTimeOff.ToUniversalTime()),
                    DxCall = qsoLogged.DXCall ?? string.Empty,
                    DxGrid = qsoLogged.DXGrid ?? string.Empty,
                    TxFrequency = qsoLogged.TXFrequencyInHz,
                    Mode = qsoLogged.Mode ?? string.Empty,
                    ReportSent = qsoLogged.ReportSent ?? string.Empty,
                    ReportReceived = qsoLogged.ReportReceived ?? string.Empty,
                    TxPower = qsoLogged.TXPower ?? string.Empty,
                    Comments = qsoLogged.Comments ?? string.Empty,
                    DatetimeOn = Timestamp.FromDateTime(qsoLogged.DateTimeOn.ToUniversalTime()),
                    OperatorCall = qsoLogged.OperatorCall ?? string.Empty,
                    MyCall = qsoLogged.MyCall ?? string.Empty,
                    MyGrid = qsoLogged.MyGrid ?? string.Empty,
                    ExchangeSent = qsoLogged.ExchangeSent ?? string.Empty,
                    ExchangeReceived = qsoLogged.ExchangeReceived ?? string.Empty,
                    AdifPropagationMode = qsoLogged.AdifPropagationMode ?? string.Empty
                };
                break;
            case WsjtxUtilsPatch.WsjtxMessages.Messages.Close:
                pbMsg.Close = new Close();
                break;
            case WsjtxUtilsPatch.WsjtxMessages.Messages.Replay:
                // Replay is an empty message in the protocol, just handle it without payload
                break;
            case WsjtxUtilsPatch.WsjtxMessages.Messages.HaltTx haltTx:
                pbMsg.HaltTx = new HaltTx
                {
                    AutoTxOnly = haltTx.AutoTxOnly
                };
                break;
            case WsjtxUtilsPatch.WsjtxMessages.Messages.FreeText freeText:
                pbMsg.FreeText = new FreeText
                {
                    Text = freeText.Text ?? string.Empty,
                    Send = freeText.Send
                };
                break;
            case WsjtxUtilsPatch.WsjtxMessages.Messages.WSPRDecode wsprDecode:
                pbMsg.WsprDecode = new WSPRDecode
                {
                    IsNew = wsprDecode.New,
                    Time = Timestamp.FromDateTime(DateTime.UtcNow.AddMilliseconds(wsprDecode.Time)),
                    Snr = wsprDecode.Snr,
                    DeltaTime = wsprDecode.DeltaTimeSeconds,
                    Frequency = wsprDecode.FrequencyHz,
                    Drift = wsprDecode.FrequencyDriftHz,
                    Callsign = wsprDecode.Callsign ?? string.Empty,
                    Grid = wsprDecode.Grid ?? string.Empty,
                    Power = wsprDecode.Power,
                    OffAir = wsprDecode.OffAir
                };
                break;
            case WsjtxUtilsPatch.WsjtxMessages.Messages.Location location:
                pbMsg.Location = new Location
                {
                    Location_ = location.LocationGridSquare ?? string.Empty
                };
                break;
            case WsjtxUtilsPatch.WsjtxMessages.Messages.LoggedAdif loggedAdif:
                pbMsg.LoggedAdif = new LoggedADIF
                {
                    AdifText = loggedAdif.AdifText ?? string.Empty
                };
                break;
            case WsjtxUtilsPatch.WsjtxMessages.Messages.HighlightCallsign highlightCallsign:
               var fg = Color.FromArgb((byte)highlightCallsign.ForegroundColor.Alpha,
                    (byte)highlightCallsign.ForegroundColor.Red,
                    (byte)highlightCallsign.ForegroundColor.Green,
                    (byte)highlightCallsign.ForegroundColor.Blue);
               
               var bg = Color.FromArgb((byte)highlightCallsign.BackgroundColor.Alpha,
                   (byte)highlightCallsign.BackgroundColor.Red,
                   (byte)highlightCallsign.BackgroundColor.Green,
                   (byte)highlightCallsign.BackgroundColor.Blue);
               
                pbMsg.HighlightCallsign = new HighlightCallsign
                {
                    Callsign = highlightCallsign.Callsign ?? string.Empty,
                    BackgroundColor = bg.ToUInt32(),
                    ForegroundColor = fg.ToUInt32(),
                    HighlightLast = highlightCallsign.HighlightLast
                };
                break;
            case WsjtxUtilsPatch.WsjtxMessages.Messages.SwitchConfiguration switchConfig:
                pbMsg.SwitchConfiguration = new SwitchConfiguration
                {
                    ConfigName = switchConfig.ConfigurationName ?? string.Empty
                };
                break;
            case WsjtxUtilsPatch.WsjtxMessages.Messages.Configure configure:
                pbMsg.Configure = new Configure
                {
                    Mode = configure.Mode ?? string.Empty,
                    FrequencyTolerance = configure.FrequencyTolerance,
                    SubMode = configure.SubMode ?? string.Empty,
                    FastMode = configure.FastMode,
                    TrPeriod = configure.TRPeriod,
                    RxDf = configure.RxDF,
                    DxCall = configure.DXCall ?? string.Empty,
                    DxGrid = configure.DXGrid ?? string.Empty,
                    GenerateMessages = configure.GenerateMessages
                };
                break;
            default:
                return null;
        }

        return pbMsg;
    }

    private static MessageType MapMessageType(WsjtxUtilsPatch.WsjtxMessages.Messages.MessageType msgType)
    {
        return msgType switch
        {
            WsjtxUtilsPatch.WsjtxMessages.Messages.MessageType.Heartbeat => MessageType.Heartbeat,
            WsjtxUtilsPatch.WsjtxMessages.Messages.MessageType.Status => MessageType.Status,
            WsjtxUtilsPatch.WsjtxMessages.Messages.MessageType.Decode => MessageType.Decode,
            WsjtxUtilsPatch.WsjtxMessages.Messages.MessageType.Clear => MessageType.Clear,
            WsjtxUtilsPatch.WsjtxMessages.Messages.MessageType.Reply => MessageType.Reply,
            WsjtxUtilsPatch.WsjtxMessages.Messages.MessageType.QSOLogged => MessageType.QsoLogged,
            WsjtxUtilsPatch.WsjtxMessages.Messages.MessageType.Close => MessageType.Close,
            WsjtxUtilsPatch.WsjtxMessages.Messages.MessageType.Replay => MessageType.Replay,
            WsjtxUtilsPatch.WsjtxMessages.Messages.MessageType.HaltTx => MessageType.HaltTx,
            WsjtxUtilsPatch.WsjtxMessages.Messages.MessageType.FreeText => MessageType.FreeText,
            WsjtxUtilsPatch.WsjtxMessages.Messages.MessageType.WSPRDecode => MessageType.WsprDecode,
            WsjtxUtilsPatch.WsjtxMessages.Messages.MessageType.Location => MessageType.Location,
            WsjtxUtilsPatch.WsjtxMessages.Messages.MessageType.LoggedADIF => MessageType.LoggedAdif,
            WsjtxUtilsPatch.WsjtxMessages.Messages.MessageType.HighlightCallsign => MessageType.HighlightCallsign,
            WsjtxUtilsPatch.WsjtxMessages.Messages.MessageType.SwitchConfiguration => MessageType.SwitchConfiguration,
            WsjtxUtilsPatch.WsjtxMessages.Messages.MessageType.Configure => MessageType.Configure,
            _ => MessageType.Heartbeat
        };
    }

    private static SpecialOperationMode MapSpecialOperationMode(WsjtxUtilsPatch.WsjtxMessages.Messages.SpecialOperationMode mode)
    {
        return mode switch
        {
            WsjtxUtilsPatch.WsjtxMessages.Messages.SpecialOperationMode.NONE => SpecialOperationMode.None,
            WsjtxUtilsPatch.WsjtxMessages.Messages.SpecialOperationMode.NAVHF => SpecialOperationMode.NaVhf,
            WsjtxUtilsPatch.WsjtxMessages.Messages.SpecialOperationMode.EUVHF => SpecialOperationMode.EuVhf,
            WsjtxUtilsPatch.WsjtxMessages.Messages.SpecialOperationMode.FIELDDAY => SpecialOperationMode.FieldDay,
            WsjtxUtilsPatch.WsjtxMessages.Messages.SpecialOperationMode.RTTYRU => SpecialOperationMode.RttyRu,
            WsjtxUtilsPatch.WsjtxMessages.Messages.SpecialOperationMode.FOX => SpecialOperationMode.Fox,
            WsjtxUtilsPatch.WsjtxMessages.Messages.SpecialOperationMode.HOUND => SpecialOperationMode.Hound,
            _ => SpecialOperationMode.None
        };
    }

    private static ClearWindow MapClearWindow(WsjtxUtilsPatch.WsjtxMessages.Messages.ClearWindow window)
    {
        return window switch
        {
            WsjtxUtilsPatch.WsjtxMessages.Messages.ClearWindow.BandActivity => ClearWindow.ClearBandActivity,
            WsjtxUtilsPatch.WsjtxMessages.Messages.ClearWindow.RxFrequency => ClearWindow.ClearRxFrequency,
            WsjtxUtilsPatch.WsjtxMessages.Messages.ClearWindow.Both => ClearWindow.ClearBoth,
            _ => ClearWindow.ClearBandActivity
        };
    }

    /// <summary>
    /// Convert CloudlogHelper RecordedCallsignDetail to protobuf RecordedCallsignDetail message.
    /// </summary>
    /// <param name="detail">The C# model to convert</param>
    /// <returns>A protobuf RecordedCallsignDetail message</returns>
    public static ClhInternalMessage ToPbRecordedCallsignDetail(RecordedCallsignDetail detail)
    {
        var pbDetail = new ClhQSOUploadStatusChanged()
        {
            Uuid = detail.Uuid,
            OriginalCountryName = detail.OriginalCountryName ?? "",
            CqZone = detail.CqZone,
            ItuZone = detail.ItuZone,
            Continent = detail.Continent ?? "",
            Latitude = detail.Latitude,
            Longitude = detail.Longitude,
            GmtOffset = detail.GmtOffset,
            Dxcc = detail.Dxcc ?? "",
            DateTimeOff = Timestamp.FromDateTime(detail.DateTimeOff.ToUniversalTime()),
            DxCall = detail.DXCall ?? "",
            DxGrid = detail.DXGrid ?? "",
            TxFrequencyInHz = detail.TXFrequencyInHz,
            TxFrequencyInMeters = detail.TXFrequencyInMeters ?? "",
            Mode = detail.Mode ?? "",
            ParentMode = detail.ParentMode ?? "",
            ReportSent = detail.ReportSent ?? "",
            ReportReceived = detail.ReportReceived ?? "",
            TxPower = detail.TXPower ?? "",
            Comments = detail.Comments ?? "",
            Name = detail.Name ?? "",
            DateTimeOn = Timestamp.FromDateTime(detail.DateTimeOn.ToUniversalTime()),
            OperatorCall = detail.OperatorCall ?? "",
            MyCall = detail.MyCall ?? "",
            MyGrid = detail.MyGrid ?? "",
            ExchangeSent = detail.ExchangeSent ?? "",
            ExchangeReceived = detail.ExchangeReceived ?? "",
            AdifPropagationMode = detail.AdifPropagationMode ?? "",
            ClientId = detail.ClientId ?? "",
            RawData = detail.RawData?.ToString() ?? "",
            FailReason = detail.FailReason ?? "",
            UploadStatus = MapUploadStatus(detail.UploadStatus),
            ForcedUpload = detail.ForcedUpload
        };

        // Copy uploaded services map
        foreach (var kvp in detail.UploadedServices)
        {
            pbDetail.UploadedServices[kvp.Key] = kvp.Value;
        }

        // Copy uploaded services error message map
        foreach (var kvp in detail.UploadedServicesErrorMessage)
        {
            pbDetail.UploadedServicesErrorMessage[kvp.Key] = kvp.Value;
        }

        var tmp = new ClhInternalMessage
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            QsoUploadStatus = pbDetail
        };

        return tmp;
    }

    /// <summary>
    /// Map CloudlogHelper UploadStatus enum to protobuf UploadStatus enum.
    /// </summary>
    /// <param name="status">The upload status to convert</param>
    /// <returns>Converted protobuf UploadStatus</returns>
    private static UploadStatus MapUploadStatus(Enums.UploadStatus status)
    {
        return status switch
        {
            Enums.UploadStatus.Pending => UploadStatus.Pending,
            Enums.UploadStatus.Uploading => UploadStatus.Uploading,
            Enums.UploadStatus.Success => UploadStatus.Success,
            Enums.UploadStatus.Fail => UploadStatus.Fail,
            Enums.UploadStatus.Ignored => UploadStatus.Ignored,
            _ => UploadStatus.Unspecified
        };
    }
}