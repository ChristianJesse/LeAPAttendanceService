namespace LeAPAttendanceService.Models;

public sealed record BiometricAttendanceRecord(
    string EmployeeId,
    DateTime Timestamp,
    string InOutStatus,
    int VerifyMode,
    int WorkCode)
{
    // This composite key helps the worker ignore duplicate rows when two polling windows overlap.
    public string UniqueKey => $"{EmployeeId}|{Timestamp:yyyy-MM-dd HH:mm:ss}|{InOutStatus}|{VerifyMode}|{WorkCode}";
}
