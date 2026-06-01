namespace LeAPAttendanceService.Services;

public interface IConfigConnection
{
    string SapDestinationName { get; }
    string SqlConnStr { get; }
    void Initialize(string sapDestinationName, string sqlConnStr);
}
