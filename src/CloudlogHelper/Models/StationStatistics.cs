using System.Text.Json.Serialization;
using CloudlogHelper.Converters;

namespace CloudlogHelper.Models;

public struct StationStatistics
{
    /// <summary>
    ///     QSOs made today.
    /// </summary>
    [JsonPropertyName("Today")]
    [JsonConverter(typeof(IntOrStringConverter))]
    public int Today { get; set; }

    /// <summary>
    ///     Total QSOs in cloudlog.
    /// </summary>
    [JsonPropertyName("total_qsos")]
    [JsonConverter(typeof(IntOrStringConverter))]
    public int TotalQsos { get; set; }

    /// <summary>
    ///     QSOs made this month.
    /// </summary>
    [JsonPropertyName("month_qsos")]
    [JsonConverter(typeof(IntOrStringConverter))]
    public int MonthQsos { get; set; }

    /// <summary>
    ///     QSOs made this year.
    /// </summary>
    [JsonPropertyName("year_qsos")]
    [JsonConverter(typeof(IntOrStringConverter))]
    public int YearQsos { get; set; }
}