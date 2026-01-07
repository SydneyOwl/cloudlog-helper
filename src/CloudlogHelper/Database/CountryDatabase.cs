using SQLite;

namespace CloudlogHelper.Database;

[Table("countries")]
public class CountryDatabase
{
    /// <summary>
    ///     Simply parse raw data from `EmbeddedCtyFilename`
    /// </summary>
    /// <param name="s"></param>
    public CountryDatabase(string s)
    {
        var info = s.Split(":");
        if (info.Length < 9) return;
        CountryName = info[0].Replace("\n", "").Trim();
        CqZone = int.Parse(info[1].Replace("\n", "").Replace(" ", ""));
        ItuZone = int.Parse(info[2].Replace("\n", "").Replace(" ", ""));
        Continent = info[3].Replace("\n", "").Replace(" ", "");
        Latitude = float.Parse(info[4].Replace("\n", "").Replace(" ", ""));
        Longitude = float.Parse(info[5].Replace("\n", "").Replace(" ", "")) * -1;
        GmtOffset = float.Parse(info[6].Replace("\n", "").Replace(" ", ""));
        Dxcc = info[7].Replace("\n", "").Replace(" ", "");
    }

    public CountryDatabase(int id, string countryName, int cqZone, int ituZone,
        string continent, float latitude, float longitude, float gmtOffset, string dxcc)
    {
        Id = id;
        CountryName = countryName;
        CqZone = cqZone;
        ItuZone = ituZone;
        Continent = continent;
        Latitude = latitude;
        Longitude = longitude;
        GmtOffset = gmtOffset;
        Dxcc = dxcc;
    }

    public CountryDatabase()
    {
    }

    /// <summary>
    ///     Primary key.
    /// </summary>
    [PrimaryKey]
    [Column("id")]
    public int? Id { get; set; }

    /// <summary>
    ///     Country name in english.
    /// </summary>
    [Column("country_name")]
    public string CountryName { get; set; } = "Unknown";

    /// <summary>
    ///     CQ Zone of current country.
    /// </summary>
    [Column("cq_zone")]
    public int CqZone { get; set; }

    /// <summary>
    ///     ITU Zone of current country.
    /// </summary>
    [Column("itu_zone")]
    public int ItuZone { get; set; }

    /// <summary>
    ///     Continent abbr of current country.
    /// </summary>
    [Column("continent")]
    public string Continent { get; set; }

    /// <summary>
    ///     Latitude in degrees, + for north.
    /// </summary>
    [Column("latitude")]
    public float Latitude { get; set; }

    /// <summary>
    ///     Longitude in degrees, + for west.
    /// </summary>
    [Column("longitude")]
    public float Longitude { get; set; }

    /// <summary>
    ///     Local time offset from GMT.
    /// </summary>
    [Column("gmt_offset")]
    public float GmtOffset { get; set; }

    /// <summary>
    ///     DXCC Prefix.
    /// </summary>
    [Column("dxcc")]
    public string Dxcc { get; set; } = "";

    public override string ToString()
    {
        return
            $"{nameof(Id)}: {Id}, {nameof(CountryName)}: {CountryName}, {nameof(CqZone)}: {CqZone}, {nameof(ItuZone)}: {ItuZone}, {nameof(Continent)}: {Continent}, {nameof(Latitude)}: {Latitude}, {nameof(Longitude)}: {Longitude}, {nameof(GmtOffset)}: {GmtOffset}, {nameof(Dxcc)}: {Dxcc}";
    }
}