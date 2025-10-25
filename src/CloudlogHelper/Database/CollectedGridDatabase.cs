using System;
using SQLite;

namespace CloudlogHelper.Database;

[Table("collected_grid")]
public class CollectedGridDatabase
{
    [PrimaryKey] [Column("callsign")] public string? Callsign { get; set; }

    [Column("grid_square")] public string? GridSquare { get; set; }

    [Column("updated_at")] public DateTime? UpdatedAt { get; set; }
}