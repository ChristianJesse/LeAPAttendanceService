


namespace LeAPAttendanceService.Models;


public sealed class LeapConfigValue
{
    public string Category { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int IsActive { get; set; }   
}


