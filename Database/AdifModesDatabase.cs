using SQLite;

namespace CloudlogHelper.Database;

[Table("adif_modes")]
public class AdifModesDatabase
{
    /// <summary>
    ///     primary key
    /// </summary>
    [PrimaryKey]
    [Column("id")]
    public int Id { get; set; }
    
    [Column("mode")]
    public string? Mode { get; set; }
    
    [Column("submode")]
    public string? SubMode { get; set; }
    
    [Column("qrgmode")]
    public string? QrgMode { get; set; }
    
    [Column("active")]
    public int Active { get; set; }
}