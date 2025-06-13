
using System;
using System.Collections.Generic;
using System.Globalization;
using ADIFLib;
using Newtonsoft.Json;

namespace CloudlogHelper.Models;

// fetched from logbookadvanced/search 
public class AdvanceQSOInfo
{
    [JsonProperty("qsoId")] 
    public string? QsoId { get; set; }

    [JsonProperty("qsoDateTime")] 
    public string? QsoDateTime { get; set; }

    [JsonProperty("de")] 
    public string? De { get; set; }

    [JsonProperty("dx")] 
    public string? Dx { get; set; }

    [JsonProperty("mode")] 
    public string? Mode { get; set; }

    [JsonProperty("rstS")] 
    public string? RstSent { get; set; }

    [JsonProperty("rstR")] 
    public string? RstReceived { get; set; }

    [JsonProperty("band")] 
    public string? Band { get; set; }
    
    
    [JsonIgnore]
    public DateTime QsoTimeOn { get; set; }
    
    [JsonIgnore]
    public ADIFQSO RawData { get; set; }

    public void ParseDatetime(string format)
    {
        if (QsoDateTime is null) return;
        QsoTimeOn = DateTime.SpecifyKind(
            DateTime.ParseExact(QsoDateTime!, format, CultureInfo.InvariantCulture),
            DateTimeKind.Utc);
    }

    public static AdvanceQSOInfo Parse(ADIFQSO adif)
    {
        var tmp = new AdvanceQSOInfo();
        tmp.RawData = adif;

        var qsoDate = "";
        var timeOn = "";
        var mode = "";
        var submode = "";
        foreach (var token in adif)
        {
            switch (token.Name)
            {
                case "call":
                    case "CALL":
                        tmp.Dx = token.Data;
                    break;
                case "mode":
                    case "MODE":
                    mode = token.Data;
                    break;
                case "submode":
                case "SUBMODE":
                    submode = token.Data;
                    break;
                case "rst_sent":
                    case "RST_SENT":
                    tmp.RstSent = token.Data;
                    break;
                case "rst_rcvd":
                    case "RST_RCVD":
                    tmp.RstReceived =  token.Data;
                    break;
                case "band":
                    case "BAND":
                    tmp.Band = token.Data;
                    break;
                case "qso_date":
                    case "QSO_DATE":
                    qsoDate = token.Data;
                    break;
                case "time_on":
                    case "TIME_ON":
                    timeOn = token.Data;
                    break;
                case "station_callsign":
                case "STATION_CALLSIGN":
                    tmp.De = token.Data;
                    break;
            }
        }

        tmp.QsoDateTime = $"{qsoDate} {timeOn}";
        tmp.ParseDatetime("yyyyMMdd HHmmss");
        
        // make sure mode refers to submode, if exists
        tmp.Mode = string.IsNullOrEmpty(submode)?mode:submode;

        return tmp;
    }
    
}

// "qsoID": "11",
// "qsoDateTime": "01/02/24 20:53",
// "de": "4W7EST",
// "dx": "SP4DH",
// "mode": "USB",
// "rstS": "59",
// "rstR": "59",
// "band": "20m",