using Newtonsoft.Json;

namespace CloudlogHelper.Models;

public struct StationStatistics
{
    /// <summary>
    ///     QSOs made today.
    /// </summary>
    [JsonProperty("Today")]
    public string Today { get; set; }

    /// <summary>
    ///     Total QSOs in cloudlog.
    /// </summary>
    [JsonProperty("total_qsos")]
    public string TotalQsos { get; set; }

    /// <summary>
    ///     QSOs made this month.
    /// </summary>
    [JsonProperty("month_qsos")]
    public string MonthQsos { get; set; }

    /// <summary>
    ///     QSOs made this year.
    /// </summary>
    [JsonProperty("year_qsos")]
    public string YearQsos { get; set; }
}