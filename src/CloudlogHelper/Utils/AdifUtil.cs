using System.Text;
using CloudlogHelper.Models;

namespace CloudlogHelper.Utils;

public class AdifUtil
{
    /// <summary>
    ///     Generate adif format log
    /// </summary>
    /// <param name="log"></param>
    /// <returns></returns>
    public static string GenerateAdifLog(AdifLog log)
    {
        var adif = new StringBuilder();
        adif.Append($"<call:{log.Call.Length}>{log.Call} ");
        adif.Append($"<gridsquare:{log.GridSquare.Length}>{log.GridSquare} ");
        adif.Append($"<mode:{log.Mode.Length}>{log.Mode} ");
        if (!string.IsNullOrEmpty(log.SubMode)) adif.Append($"<submode:{log.SubMode.Length}>{log.SubMode} ");
        adif.Append($"<rst_sent:{log.RstSent.Length}>{log.RstSent} ");
        adif.Append($"<rst_rcvd:{log.RstRcvd.Length}>{log.RstRcvd} ");
        adif.Append($"<qso_date:{log.QsoDate.Length}>{log.QsoDate} ");
        adif.Append($"<time_on:{log.TimeOn.Length}>{log.TimeOn} ");
        adif.Append($"<qso_date_off:{log.QsoDateOff.Length}>{log.QsoDateOff} ");
        adif.Append($"<time_off:{log.TimeOff.Length}>{log.TimeOff} ");
        adif.Append($"<band:{log.Band.Length}>{log.Band} ");
        adif.Append($"<freq:{log.Freq.Length}>{log.Freq} ");
        adif.Append($"<station_callsign:{log.StationCallsign.Length}>{log.StationCallsign} ");
        adif.Append($"<my_gridsquare:{log.MyGridSquare.Length}>{log.MyGridSquare} ");
        if (!string.IsNullOrEmpty(log.Comment)) adif.Append($"<comment:{log.Comment.Length}>{log.Comment} ");
        adif.Append("<eor>");
        return adif.ToString();
    }
    
    private static string EscapeAdif(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        return input
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("'", "&apos;")
            .Replace("\"", "&quot;");
    }
}