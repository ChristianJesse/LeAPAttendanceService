namespace LeAPAttendanceService;

public sealed class SqlRegistryOptions
{
    public const string SectionName = "SqlRegistry";

    public string Module { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
}
