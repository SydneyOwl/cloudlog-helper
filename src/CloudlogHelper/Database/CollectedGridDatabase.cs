using SQLite;

namespace CloudlogHelper.Database;

[Table("collected_grid")]
public class CollectedGridDatabase
{
    [PrimaryKey]
    [AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Column("callsign")]
    [Unique]
    public string? Callsign { get; set; }
    
    [Column("grid_square")]
    public string? GridSquare { get; set; }
}