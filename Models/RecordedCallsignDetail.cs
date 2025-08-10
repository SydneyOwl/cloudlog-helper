using System;
using System.Collections.Generic;
using System.Globalization;
using ADIFLib;
using CloudlogHelper.Database;
using CloudlogHelper.Utils;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Models;

public enum UploadStatus
{
    Uploading,
    Retrying,
    Pending,
    Success,
    Fail,
    Ignored // Auto upload qso is not enabled 
}

public class RecordedCallsignDetail : ReactiveObject
{
    /// <summary>
    ///     Localized country names.
    /// </summary>
    public string LocalizedCountryName { get; set; }

    /// <summary>
    ///     CQ Zone.
    /// </summary>
    public int CqZone { get; set; }

    /// <summary>
    ///     ITU Zone.
    /// </summary>
    public int ItuZone { get; set; }

    /// <summary>
    ///     Continent abbr like `AS` `EU`
    /// </summary>
    public string Continent { get; set; }

    /// <summary>
    ///     Latitude in degrees, + for north
    /// </summary>
    public float Latitude { get; set; }

    /// <summary>
    ///     Longitude in degrees, + for west
    /// </summary>
    public float Longitude { get; set; }

    /// <summary>
    ///     Local time offset from GMT
    /// </summary>
    public float GmtOffset { get; set; }

    /// <summary>
    ///     DXCC Perfix
    /// </summary>
    public string Dxcc { get; set; } = "";

    /// <summary>
    ///     End qso time.
    /// </summary>
    public DateTime DateTimeOff { get; set; }

    /// <summary>
    ///     Value of the DX Call field
    /// </summary>
    public string DXCall { get; set; }

    /// <summary>
    ///     Value of the DX grid field
    /// </summary>
    public string DXGrid { get; set; }

    /// <summary>
    ///     TX Frequency (Hz)
    /// </summary>
    public ulong TXFrequencyInHz { get; set; }
    
    /// <summary>
    ///     Tx frequency in meters like `20m`
    /// </summary>
    public string TXFrequencyInMeters { get; set; }

    /// <summary>
    ///     WSJT-X Operating mode
    /// </summary>
    public string Mode { get; set; }

    /// <summary>
    ///     WSJT-X Operating mode's parent mode
    /// </summary>
    public string ParentMode { get; set; }

    /// <summary>
    ///     Signal report sent
    /// </summary>
    public string ReportSent { get; set; }

    /// <summary>
    ///     Signal report received
    /// </summary>
    public string ReportReceived { get; set; }

    /// <summary>
    ///     TX power
    /// </summary>
    public string TXPower { get; set; }

    /// <summary>
    ///     Comments field
    /// </summary>
    public string Comments { get; set; }

    /// <summary>
    ///     Remote operator's name
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     Date and time of the start of the QSO
    /// </summary>
    public DateTime DateTimeOn { get; set; }

    /// <summary>
    ///     Local operator's call sign
    /// </summary>
    public string OperatorCall { get; set; }

    /// <summary>
    ///     Call sign sent
    /// </summary>
    public string MyCall { get; set; }

    /// <summary>
    ///     Madienhead gridsquare sent
    /// </summary>
    public string MyGrid { get; set; }

    /// <summary>
    ///     Exchange message sent
    /// </summary>
    public string ExchangeSent { get; set; }

    /// <summary>
    ///     Exchange message received
    /// </summary>
    public string ExchangeReceived { get; set; }

    /// <summary>
    ///     Propagation mode
    /// </summary>
    public string AdifPropagationMode { get; set; }

    /// <summary>
    ///     Client ID sent by client, e.g. `JTDX` `WSJT-X`
    /// </summary>
    public string ClientId { get; set; }
    
    
    /// <summary>
    /// Original adif data.
    /// </summary>
    public ADIFQSO? RawData { get; set; }

    /// <summary>
    ///     The fail reason of current upload.
    /// </summary>
    [Reactive]
    public string? FailReason { get; set; }

    /// <summary>
    ///     True if specified item is checked.
    /// </summary>
    [Reactive]
    public bool Checked { get; set; }

    /// <summary>
    ///     Upload status of log services.
    /// </summary>
    public Dictionary<string, bool> UploadedServices = new();

    /// <summary>
    ///     Upload status of current item.
    /// </summary>
    [Reactive]
    public UploadStatus UploadStatus { get; set; } = UploadStatus.Pending;


    /// <summary>
    ///     Whether to force-upload this qso or not, even if auto upload function is not enabled.
    /// </summary>
    public bool ForcedUpload { get; set; }


    /// <summary>
    ///     Generate a RecordedCallsignDetail object by passing CountryDatabase and QsoLogged.
    /// </summary>
    /// <param name="cdb"></param>
    /// <param name="qlo"></param>
    /// <returns></returns>
    public static RecordedCallsignDetail GenerateCallsignDetail(CountryDatabase cdb, QsoLogged qlo)
    {
        return new RecordedCallsignDetail
        {
            LocalizedCountryName = ApplicationSettings.GetInstance().LanguageType == SupportedLanguage.SimplifiedChinese
                ? cdb.CountryNameCn
                : cdb.CountryNameEn,
            CqZone = cdb.CqZone,
            ItuZone = cdb.ItuZone,
            Continent = cdb.Continent,
            Latitude = cdb.Latitude,
            Longitude = cdb.Longitude,
            GmtOffset = cdb.GmtOffset,
            Dxcc = cdb.Dxcc,
            DateTimeOff = qlo.DateTimeOff,
            DXCall = qlo.DXCall,
            DXGrid = qlo.DXGrid,
            TXFrequencyInHz = qlo.TXFrequencyInHz,
            TXFrequencyInMeters = FreqHelper.GetMeterFromFreq(qlo.TXFrequencyInHz),
            Mode = qlo.Mode,
            ReportSent = qlo.ReportSent,
            ReportReceived = qlo.ReportReceived,
            TXPower = qlo.TXPower,
            Comments = qlo.Comments,
            Name = qlo.Name,
            DateTimeOn = qlo.DateTimeOn,
            OperatorCall = qlo.OperatorCall,
            MyCall = qlo.MyCall,
            MyGrid = qlo.MyGrid,
            ExchangeSent = qlo.ExchangeSent,
            ExchangeReceived = qlo.ExchangeReceived,
            AdifPropagationMode = qlo.AdifPropagationMode,
            ClientId = string.IsNullOrEmpty(qlo.Id)
                ? string.Empty
                : qlo.Id[..(qlo.Id.Length > 6 ? 6 : qlo.Id.Length)],
            UploadStatus = UploadStatus.Pending
        };
    }

    public static RecordedCallsignDetail Parse(AdifLog info, bool markAsOldQso = true)
    {
        ulong hzValue = 0;
        if (double.TryParse(info.Freq, out var mhzValue))
        {
            hzValue = (ulong)(mhzValue * 1_000_000);
        }
        return new RecordedCallsignDetail
        {
            LocalizedCountryName = markAsOldQso?"Local Log":"?",
            DXCall = info.Call!,
            MyCall = info.StationCallsign,
            ReportSent = info.RstSent!,
            ReportReceived = info.RstRcvd!,
            TXFrequencyInMeters = info.Band!,
            TXFrequencyInHz = hzValue,
            DateTimeOn = DateTime.ParseExact($"{info.QsoDate} {info.TimeOn}","yyyyMMdd HHmmss",CultureInfo.InvariantCulture),
            Mode = string.IsNullOrEmpty(info.SubMode)?info.Mode:info.SubMode,
            ClientId = "LOCAL",
            RawData = info.RawData,
            FailReason = null,
            Checked = false,
            UploadStatus = UploadStatus.Pending,
            ForcedUpload = false
        };
    }

    public string? GenerateAdif()
    {
        var rcd = this;
        try
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
        catch (Exception e)
        {
            return null;
        }
    }

    public override string ToString()
    {
        return
            $"{nameof(LocalizedCountryName)}: {LocalizedCountryName}, {nameof(CqZone)}: {CqZone}, {nameof(ItuZone)}: {ItuZone}, {nameof(Continent)}: {Continent}, {nameof(Latitude)}: {Latitude}, {nameof(Longitude)}: {Longitude}, {nameof(GmtOffset)}: {GmtOffset}, {nameof(Dxcc)}: {Dxcc}, {nameof(DateTimeOff)}: {DateTimeOff}, {nameof(DXCall)}: {DXCall}, {nameof(DXGrid)}: {DXGrid}, {nameof(TXFrequencyInHz)}: {TXFrequencyInHz}, {nameof(TXFrequencyInMeters)}: {TXFrequencyInMeters}, {nameof(Mode)}: {Mode}, {nameof(ParentMode)}: {ParentMode}, {nameof(ReportSent)}: {ReportSent}, {nameof(ReportReceived)}: {ReportReceived}, {nameof(TXPower)}: {TXPower}, {nameof(Comments)}: {Comments}, {nameof(Name)}: {Name}, {nameof(DateTimeOn)}: {DateTimeOn}, {nameof(OperatorCall)}: {OperatorCall}, {nameof(MyCall)}: {MyCall}, {nameof(MyGrid)}: {MyGrid}, {nameof(ExchangeSent)}: {ExchangeSent}, {nameof(ExchangeReceived)}: {ExchangeReceived}, {nameof(AdifPropagationMode)}: {AdifPropagationMode}, {nameof(ClientId)}: {ClientId}, {nameof(Checked)}: {Checked}, {nameof(UploadStatus)}: {UploadStatus}";
    }

    protected bool Equals(RecordedCallsignDetail other)
    {
        return DateTimeOff.Equals(other.DateTimeOff) && DXCall == other.DXCall && DXGrid == other.DXGrid &&
               TXFrequencyInHz == other.TXFrequencyInHz && Mode == other.Mode && ReportSent == other.ReportSent &&
               ReportReceived == other.ReportReceived && DateTimeOn.Equals(other.DateTimeOn) &&
               MyCall == other.MyCall && MyGrid == other.MyGrid && ClientId == other.ClientId;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((RecordedCallsignDetail)obj);
    }

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(DateTimeOff);
        hashCode.Add(DXCall);
        hashCode.Add(DXGrid);
        hashCode.Add(TXFrequencyInHz);
        hashCode.Add(Mode);
        hashCode.Add(ReportSent);
        hashCode.Add(ReportReceived);
        hashCode.Add(DateTimeOn);
        hashCode.Add(MyCall);
        hashCode.Add(MyGrid);
        hashCode.Add(ClientId);
        return hashCode.ToHashCode();
    }

    /// <summary>
    ///     Check if this qso is uploadable..
    /// </summary>
    /// <returns></returns>
    public bool IsUploadable()
    {
        return UploadStatus is UploadStatus.Ignored or UploadStatus.Fail or UploadStatus.Pending;
    }
}