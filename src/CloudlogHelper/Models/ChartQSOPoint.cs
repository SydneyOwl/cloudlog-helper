namespace CloudlogHelper.Models;

public struct ChartQSOPoint
{
    /// <summary>
    /// Callsign of DX
    /// </summary>
    public string DxCallsign { get; set; }
    /// <summary>
    /// Degrees
    /// </summary>
    public double Azimuth { get; set; }
    /// <summary>
    /// Distance in km
    /// </summary>
    public double Distance { get; set; }
    public string Mode { get; set; }
    public int Snr { get; set; }
    /// <summary>
    /// Current band
    /// </summary>
    public string Band { get; set; }
    public string Client { get; set; }

    public override string ToString()
    {
        return
            $"{nameof(DxCallsign)}: {DxCallsign}, {nameof(Azimuth)}: {Azimuth}, {nameof(Distance)}: {Distance}, {nameof(Mode)}: {Mode}, {nameof(Snr)}: {Snr}, {nameof(Band)}: {Band}, {nameof(Client)}: {Client}";
    }
}