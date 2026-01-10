using System;
using CloudlogHelper.Models;
using SQLite;

namespace CloudlogHelper.Database;

[Table("ignored_qsos")]
public class IgnoredQsoDatabase
{
    [PrimaryKey]
    [AutoIncrement]
    [Column("id")]
    public int? Id { get; set; }

    [Indexed("IX_IgnoredQso_Query", 1)]
    [Column("de")] 
    public string? De { get; set; }

    [Indexed("IX_IgnoredQso_Query", 2)]
    [Column("dx")] 
    public string? Dx { get; set; }

    [Column("freq")] 
    public string? Freq { get; set; }

    /// <summary>
    ///     if a adif has submode, then final mode is submode. otherwise it'll be mode.
    /// </summary>
    [Indexed("IX_IgnoredQso_Query", 3)]
    [Column("final_mode")]
    public string? FinalMode { get; set; }

    [Indexed("IX_IgnoredQso_Query", 4)]
    [Column("rst_sent")] 
    public string? RstSent { get; set; }

    [Indexed("IX_IgnoredQso_Query", 5)]
    [Column("rst_recv")] 
    public string? RstRecv { get; set; }

    [Column("qso_start_time")] 
    public DateTime? QsoStartTime { get; set; }

    [Column("updated_at")] 
    public DateTime? UpdatedAt { get; set; }

    public static IgnoredQsoDatabase Parse(RecordedCallsignDetail detail)
    {
        return new IgnoredQsoDatabase
        {
            De = detail.DXCall,
            Dx = detail.MyCall,
            Freq = (detail.TXFrequencyInHz / 1_000_000.0).ToString("0.000000"),
            FinalMode = detail.Mode,
            RstSent = detail.ReportSent,
            RstRecv = detail.ReportReceived,
            QsoStartTime = detail.DateTimeOn
        };
    }

    public override string ToString()
    {
        return
            $"{nameof(Id)}: {Id}, {nameof(De)}: {De}, {nameof(Dx)}: {Dx}, {nameof(Freq)}: {Freq}, {nameof(FinalMode)}: {FinalMode}, {nameof(RstSent)}: {RstSent}, {nameof(RstRecv)}: {RstRecv}, {nameof(QsoStartTime)}: {QsoStartTime}";
    }
}