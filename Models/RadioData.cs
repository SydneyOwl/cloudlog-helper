namespace CloudlogHelper.Models;

public struct RadioData
{
    /// <summary>
    /// Is rig in split mode?
    /// </summary>
    public bool IsSplit { get; set; }
    
    /// <summary>
    ///     Frequency in Hz.
    /// </summary>
    public long FrequencyTx { get; set; }
    
    /// <summary>
    ///     Mode of the rig.
    /// </summary>
    public string ModeTx { get; set; }

    /// <summary>
    /// Optional Rx frequency in Hz.
    /// </summary>
    public long FrequencyRx { get; set; }

    /// <summary>
    /// Optional Rx mode (not logged).
    /// </summary>
    public string ModeRx { get; set; }
    
    /// <summary>
    /// Optional transmit power in Watts.
    /// </summary>
    public float Power { get; set; }
}