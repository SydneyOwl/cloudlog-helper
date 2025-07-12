using System;
using System.Collections.Generic;
using System.Globalization;
using ADIFLib;
using CloudlogHelper.Resources;

namespace CloudlogHelper.Models;

public class AdifLog
{
    public string Call { get; set; }
    public string GridSquare { get; set; }
    public string Mode { get; set; }
    public string SubMode { get; set; }
    public string RstSent { get; set; }
    public string RstRcvd { get; set; }
    public string QsoDate { get; set; }
    public string TimeOn { get; set; }
    public string QsoDateOff { get; set; }
    public string TimeOff { get; set; }
    public string Band { get; set; }
    public string Freq { get; set; }
    public string StationCallsign { get; set; }
    public string MyGridSquare { get; set; }
    public string Comment { get; set; }

    public ADIFQSO RawData { get; set; }

    public static AdifLog Parse(ADIFQSO adif)
    {
        var tmp = new AdifLog();
        tmp.RawData = adif;
        foreach (var token in adif)
            switch (token.Name)
            {
                case "call":
                case "CALL":
                    tmp.Call = token.Data.Trim();
                    break;
                case "mode":
                case "MODE":
                    tmp.Mode = token.Data.Trim();
                    break;
                case "submode":
                case "SUBMODE":
                    tmp.SubMode = token.Data.Trim();
                    break;
                case "rst_sent":
                case "RST_SENT":
                    tmp.RstSent = token.Data.Trim();
                    break;
                case "rst_rcvd":
                case "RST_RCVD":
                    tmp.RstRcvd = token.Data.Trim();
                    break;
                case "band":
                case "BAND":
                    tmp.Band = token.Data.Trim();
                    break;
                case "qso_date":
                case "QSO_DATE":
                    tmp.QsoDate = token.Data.Trim();
                    break;
                case "time_on":
                case "TIME_ON":
                    tmp.TimeOn = token.Data.Trim();
                    break;
                case "station_callsign":
                case "STATION_CALLSIGN":
                    tmp.StationCallsign = token.Data.Trim();
                    break;
                case "freq":
                case "FREQ":
                    tmp.Freq = token.Data.Trim();
                    break;
                case "qso_date_off":
                case "QSO_DATE_OFF":
                    tmp.QsoDateOff = token.Data.Trim();
                    break;
                case "time_off":
                case "TIME_OFF":
                    tmp.TimeOff = token.Data.Trim();
                    break;
                case "my_gridsquare":
                case "MY_GRIDSQUARE":
                    tmp.MyGridSquare = token.Data.Trim();
                    break;
            }

        return tmp;
    }

    private sealed class AdifLogEqualityComparer : IEqualityComparer<AdifLog>
    {
        public bool Equals(AdifLog? x, AdifLog? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null) return false;
            if (y is null) return false;
            if (x.GetType() != y.GetType()) return false;
            return x.Call == y.Call &&
                   ((x.Mode == y.Mode && x.SubMode == y.SubMode) || x.Mode == y.SubMode || x.SubMode == y.Mode) &&
                   x.RstSent == y.RstSent && x.RstRcvd == y.RstRcvd && x.QsoDate == y.QsoDate && x.Band == y.Band &&
                   Math.Abs(float.Parse(x.Freq) - float.Parse(y.Freq)) < DefaultConfigs.AllowedFreqOffsetMHz &&
                   Math.Abs((DateTime.ParseExact(x.TimeOn, "HHmmss", CultureInfo.InvariantCulture) -
                             DateTime.ParseExact(y.TimeOn, "HHmmss", CultureInfo.InvariantCulture)).Minutes) <
                   DefaultConfigs.AllowedTimeOffsetMinutes;
        }

        public int GetHashCode(AdifLog obj)
        {
            // return 0;
            var hashCode = new HashCode();
            hashCode.Add(obj.Call);
            hashCode.Add(obj.RstSent);
            hashCode.Add(obj.RstRcvd);
            hashCode.Add(obj.QsoDate);
            return hashCode.ToHashCode();
        }
    }

    public static IEqualityComparer<AdifLog> AdifLogComparer { get; } = new AdifLogEqualityComparer();
}