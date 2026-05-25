namespace LeAPAttendanceService;

public sealed class BiometricOptions
{
    public const string SectionName = "Biometric";

    public bool Enabled { get; set; }

    // Network address of the biometric device.
    public string DeviceIp { get; set; } = "192.168.3.61";

    // Default ZKTeco devices usually listen on 4370.
    public int Port { get; set; } = 4370;

    // Most installations use machine number 1 unless the device was configured differently.
    public int MachineNumber { get; set; } = 1;

    // How often the worker should reconnect and read new attendance logs.
    public int PollingIntervalSeconds { get; set; } = 10;

    // On the first run we read a small history window so we can catch recent punches.
    public int InitialLookBackMinutes { get; set; } = 1440;

    // Mirrors your old workflow: sync the device clock first, then read logs.
    public bool SyncTimeOnStartup { get; set; }

    // Kept mainly for diagnostics so the service can tell you where it expects the SDK files to live.
    public string SdkFolder { get; set; } = @"Sdk\TSDK";
}
