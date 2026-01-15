using System.Text.Json.Serialization;
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
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [Column("mode")] 
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [Column("submode")]
    [JsonPropertyName("submode")]
    public string? SubMode { get; set; }

    [Column("qrgmode")] 
    [JsonPropertyName("qrgmode")]
    public string? QrgMode { get; set; }

    [Column("active")] 
    [JsonPropertyName("active")] 
    public int Active { get; set; }
}