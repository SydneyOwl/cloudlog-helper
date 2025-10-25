using System;
using System.Collections.Generic;

namespace CloudlogHelper.Models;

public struct ChartQSOPoint
{
    public bool IsAccurate;

    /// <summary>
    ///     Callsign of DX
    /// </summary>
    public string DxCallsign { get; set; }

    /// <summary>
    ///     DXCC
    /// </summary>
    public string DXCC { get; set; }

    /// <summary>
    ///     Degrees
    /// </summary>
    public double Azimuth { get; set; }

    /// <summary>
    ///     Distance in km
    /// </summary>
    public double Distance { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Mode { get; set; }
    public int Snr { get; set; }

    /// <summary>
    ///     Current band
    /// </summary>
    public string Band { get; set; }

    public string Client { get; set; }

    public override string ToString()
    {
        return
            $"{nameof(DxCallsign)}: {DxCallsign}, {nameof(DXCC)}: {DXCC}, {nameof(Azimuth)}: {Azimuth}, {nameof(Distance)}: {Distance}, {nameof(Latitude)}: {Latitude}, {nameof(Longitude)}: {Longitude}, {nameof(Mode)}: {Mode}, {nameof(Snr)}: {Snr}, {nameof(Band)}: {Band}, {nameof(Client)}: {Client}";
    }

    private sealed class ChartQsoPointEqualityComparer : IEqualityComparer<ChartQSOPoint>
    {
        public bool Equals(ChartQSOPoint x, ChartQSOPoint y)
        {
            return x.DxCallsign == y.DxCallsign && x.Mode == y.Mode && x.Band == y.Band && x.Client == y.Client;
        }

        public int GetHashCode(ChartQSOPoint obj)
        {
            return HashCode.Combine(obj.DxCallsign, obj.Mode, obj.Band, obj.Client);
        }
    }

    public static IEqualityComparer<ChartQSOPoint> ChartQsoPointComparer { get; } = new ChartQsoPointEqualityComparer();
}