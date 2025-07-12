using System;
using SQLite;

namespace CloudlogHelper.Database;

[Table("ignored_qsos")]
public class IgnoredQsoDatabase
{
    [PrimaryKey]
    [AutoIncrement]
    [Column("id")]
    public int Id { get; set; }
    
    [Column("de")]
    public string? De { get; set; }
    
    [Column("dx")]
    public string? Dx { get; set; }
    
    [Column("freq")]
    public string? Freq { get; set; }
   
    /// <summary>
    /// if a adif has submode, then final mode is submode. otherwise it'll be mode.
    /// </summary>
    [Column("final_mode")]
    public string? FinalMode { get; set; }
    
    [Column("rst_sent")] 
    public string? RstSent { get; set; }
    
    [Column("rst_recv")]
    public string? RstRecv { get; set; }
    
    [Column("qso_start_time")]
    public DateTime? QsoStartTime { get; set; }
    
    // public bool Is
}