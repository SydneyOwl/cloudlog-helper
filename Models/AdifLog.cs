namespace CloudlogHelper.Models;

public class AdifLog
{
    public string Call { get; set; }
    public string GridSquare{ get; set; }
    public string Mode{ get; set; }
    public string SubMode{ get; set; }
    public string RstSent{ get; set; }
    public string RstRcvd{ get; set; }
    public string QsoDate{ get; set; }
    public string TimeOn{ get; set; }
    public string QsoDateOff{ get; set; }
    public string TimeOff{ get; set; }
    public string Band{ get; set; }
    public string Freq{ get; set; }
    public string StationCallsign{ get; set; }
    public string MyGridSquare{ get; set; }
    public string Comment{ get; set; }
}