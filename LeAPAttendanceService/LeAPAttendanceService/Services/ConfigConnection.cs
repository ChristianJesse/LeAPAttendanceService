namespace LeAPAttendanceService.Services;

public sealed class ConfigConnection : IConfigConnection
{
    private bool _initialized;
    private readonly object _lock = new();

    public string SapDestinationName { get; private set; } = string.Empty;
    public string SqlConnStr { get; private set; } = string.Empty;

    public void Initialize(string sapDestinationName, string sqlConnStr)
    {
        lock (_lock)
        {
            if (_initialized)
            {
                return;
            }

            SapDestinationName = sapDestinationName;
            SqlConnStr = sqlConnStr;
            _initialized = true;
        }
    }
}
