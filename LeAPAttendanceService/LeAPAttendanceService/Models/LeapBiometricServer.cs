namespace LeAPAttendanceService.Models;

public sealed class LeapBiometricServer
{
    public int ServerID { get; set; }
    public string IPAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public string? Office { get; set; }
    public string? Description { get; set; }
}
