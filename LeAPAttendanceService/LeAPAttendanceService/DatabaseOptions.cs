namespace LeAPAttendanceService;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = string.Empty;

    public string AttendanceRecordsTypeName { get; set; } = "dbo.TypeLEAPInOutRecords";
}
