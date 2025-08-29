using SQLite;

namespace CloudlogHelper.Database;

[Table("callsigns")]
public class CallsignDatabase
{
    /// <summary>
    ///     primary key
    /// </summary>
    [PrimaryKey]
    [AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>
    ///     Id of the item.
    /// </summary>
    [Column("country_id")]
    public int CountryId { get; set; }

    /// <summary>
    ///     prefix of the callsign. Used for callsign matching.
    /// </summary>
    [Column("callsign")]
    public string Callsign { get; set; } = "";
}