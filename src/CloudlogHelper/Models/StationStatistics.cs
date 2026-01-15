using System.Text.Json.Serialization;

namespace CloudlogHelper.Models;

public struct StationStatistics
{
    /// <summary>
    ///     QSOs made today.
    /// </summary>
    [JsonPropertyName("Today")]
    public string Today { get; set; }

    /// <summary>
    ///     Total QSOs in cloudlog.
    /// </summary>
    [JsonPropertyName("total_qsos")]
    public string TotalQsos { get; set; }

    /// <summary>
    ///     QSOs made this month.
    /// </summary>
    [JsonPropertyName("month_qsos")]
    public string MonthQsos { get; set; }

    /// <summary>
    ///     QSOs made this year.
    /// </summary>
    [JsonPropertyName("year_qsos")]
    public string YearQsos { get; set; }
}