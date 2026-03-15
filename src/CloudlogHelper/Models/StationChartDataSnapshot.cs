using System.Collections.Generic;
using ScottPlot.Statistics;

namespace CloudlogHelper.Models;

public sealed class StationChartDataSnapshot
{
    public StationChartDataSnapshot(
        Dictionary<string, double?> stationCountByDxcc,
        Histogram distanceHistogram,
        Histogram bearingHistogram,
        double[,] gridStationCount)
    {
        StationCountByDxcc = stationCountByDxcc;
        DistanceHistogram = distanceHistogram;
        BearingHistogram = bearingHistogram;
        GridStationCount = gridStationCount;
    }

    public Dictionary<string, double?> StationCountByDxcc { get; }
    public Histogram DistanceHistogram { get; }
    public Histogram BearingHistogram { get; }
    public double[,] GridStationCount { get; }
}
