using SQLite;

namespace CloudlogHelper.Database;

[Table("application_version")]
public class ApplicationVersionDatabase
{
    [PrimaryKey] [Column("id")] public int Id { get; set; } = 1;
    [Column("current_version")] public string CurrentVersion { get; set; } = "0.0.0";

    public static ApplicationVersionDatabase NewDefaultAppVersion()
    {
        return new ApplicationVersionDatabase
        {
            CurrentVersion = "0.0.0",
            Id = 1
        };
    }

    public static ApplicationVersionDatabase NewAppVersionWithVersionNumber(string versionNumber)
    {
        return new ApplicationVersionDatabase
        {
            CurrentVersion = versionNumber,
            Id = 1
        };
    }
}