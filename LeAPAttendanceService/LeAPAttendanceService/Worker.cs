namespace LeAPAttendanceService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IBiometricService _biometricService;
    private readonly BiometricOptions _biometricOptions;
    private HashSet<string> _lastBatchKeys = [];
    private DateTime _nextReadFrom;

    public Worker(
        ILogger<Worker> logger,
        IBiometricService biometricService,
        Microsoft.Extensions.Options.IOptions<BiometricOptions> biometricOptions)
    {
        _logger = logger;
        _biometricService = biometricService;
        _biometricOptions = biometricOptions.Value;

        // Start slightly in the past so the first poll can pick up recent logs after the service starts.
        _nextReadFrom = DateTime.Now.AddMinutes(-Math.Abs(_biometricOptions.InitialLookBackMinutes));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine(
            $"[LeAPAttendanceService] Worker started. Device={_biometricOptions.DeviceIp}:{_biometricOptions.Port}, " +
            $"MachineNumber={_biometricOptions.MachineNumber}, PollingIntervalSeconds={_biometricOptions.PollingIntervalSeconds}, " +
            $"InitialLookBackMinutes={_biometricOptions.InitialLookBackMinutes}, SyncTimeOnStartup={_biometricOptions.SyncTimeOnStartup}");

        _logger.LogInformation(
            "LeAP Attendance worker started. Device: {DeviceIp}:{Port}, MachineNumber: {MachineNumber}, PollingIntervalSeconds: {PollingIntervalSeconds}, InitialLookBackMinutes: {InitialLookBackMinutes}, SyncTimeOnStartup: {SyncTimeOnStartup}",
            _biometricOptions.DeviceIp,
            _biometricOptions.Port,
            _biometricOptions.MachineNumber,
            _biometricOptions.PollingIntervalSeconds,
            _biometricOptions.InitialLookBackMinutes,
            _biometricOptions.SyncTimeOnStartup);

        if (!_biometricOptions.Enabled)
        {
            Console.WriteLine($"[LeAPAttendanceService] Biometric polling is disabled. Set {BiometricOptions.SectionName}:{nameof(BiometricOptions.Enabled)} to true.");
            _logger.LogInformation(
                "Biometric polling is disabled. Set {Section}:{Key} to true when the device details are ready.",
                BiometricOptions.SectionName,
                nameof(BiometricOptions.Enabled));
            return;
        }

        if (_biometricOptions.SyncTimeOnStartup)
        {
            try
            {
                Console.WriteLine($"[LeAPAttendanceService] Synchronizing device time for {_biometricOptions.DeviceIp}:{_biometricOptions.Port}.");
                _logger.LogInformation(
                    "Synchronizing biometric device time before the first attendance read. Device: {DeviceIp}:{Port}",
                    _biometricOptions.DeviceIp,
                    _biometricOptions.Port);

                await _biometricService.SyncDeviceTimeAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                Console.WriteLine($"[LeAPAttendanceService] Device time synchronization failed: {ex.Message}");
                _logger.LogError(ex, "Biometric device time synchronization failed.");
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Console.WriteLine($"[LeAPAttendanceService] Starting biometric poll from {_nextReadFrom:yyyy-MM-dd HH:mm:ss}.");
                _logger.LogInformation(
                    "Starting biometric poll. Reading records from device {DeviceIp}:{Port} from {FromTime:yyyy-MM-dd HH:mm:ss}.",
                    _biometricOptions.DeviceIp,
                    _biometricOptions.Port,
                    _nextReadFrom);

                var records = await _biometricService.GetAttendanceLogsAsync(_nextReadFrom, stoppingToken);
                var newRecords = records.Where(record => !_lastBatchKeys.Contains(record.UniqueKey)).ToList();

                Console.WriteLine($"[LeAPAttendanceService] Device returned {records.Count} record(s).");
                if (records.Count > 0)
                {
                    Console.WriteLine("ServerID\tIDNumber\tInOutStatus\tTimeInOut");
                }

                foreach (var record in records)
                {
                    var recordState = _lastBatchKeys.Contains(record.UniqueKey) ? "EXISTING" : "NEW";
                    Console.WriteLine(
                        $"{_biometricOptions.DeviceIp}\t{record.EmployeeId}\t{record.InOutStatus}\t{record.Timestamp:yyyy-MM-dd HH:mm:ss.000}");
                    Console.WriteLine(
                        $"[LeAPAttendanceService] Device Data => State={recordState}, EmployeeId={record.EmployeeId}, Status={record.InOutStatus}, " +
                        $"Timestamp={record.Timestamp:yyyy-MM-dd HH:mm:ss}, VerifyMode={record.VerifyMode}, WorkCode={record.WorkCode}");
                }

                foreach (var record in newRecords)
                {
                    Console.WriteLine(
                        $"[LeAPAttendanceService] Retrieved Record => EmployeeId={record.EmployeeId}, Status={record.InOutStatus}, " +
                        $"Timestamp={record.Timestamp:yyyy-MM-dd HH:mm:ss}, VerifyMode={record.VerifyMode}, WorkCode={record.WorkCode}");

                    _logger.LogInformation(
                        "Biometric log captured. Employee: {EmployeeId}, Status: {Status}, Time: {Timestamp:yyyy-MM-dd HH:mm:ss}, VerifyMode: {VerifyMode}, WorkCode: {WorkCode}",
                        record.EmployeeId,
                        record.InOutStatus,
                        record.Timestamp,
                        record.VerifyMode,
                        record.WorkCode);
                }

                _lastBatchKeys = records.Select(record => record.UniqueKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (records.Count > 0)
                {
                    // Keep a one-second overlap so we do not miss records written at the same second
                    // as the last successful row read from the device.
                    _nextReadFrom = records.Max(record => record.Timestamp).AddSeconds(-1);
                }
                else
                {
                    _nextReadFrom = DateTime.Now.AddSeconds(-1);
                }

                if (records.Count == 0)
                {
                    Console.WriteLine($"[LeAPAttendanceService] Poll succeeded. No attendance records found from {_nextReadFrom:yyyy-MM-dd HH:mm:ss}.");
                    _logger.LogInformation(
                        "Biometric poll succeeded but no attendance records were found from {FromTime:yyyy-MM-dd HH:mm:ss}.",
                        _nextReadFrom);
                }

                Console.WriteLine($"[LeAPAttendanceService] Poll completed. TotalRecords={records.Count}, NewRecords={newRecords.Count}.");
                _logger.LogInformation(
                    "Biometric poll completed. Retrieved {TotalCount} records, {NewCount} of them were new for this worker instance.",
                    records.Count,
                    newRecords.Count);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                Console.WriteLine($"[LeAPAttendanceService] Poll failed: {ex.Message}");
                _logger.LogError(
                    ex,
                    "Biometric poll failed for device {DeviceIp}:{Port}. Common causes: SDK not registered, device offline, or firewall blocking port {Port}.",
                    _biometricOptions.DeviceIp,
                    _biometricOptions.Port,
                    _biometricOptions.Port);
            }

            Console.WriteLine($"[LeAPAttendanceService] Waiting {Math.Max(5, _biometricOptions.PollingIntervalSeconds)} seconds before next poll.");
            _logger.LogInformation(
                "Waiting {DelaySeconds} seconds before the next biometric poll.",
                Math.Max(5, _biometricOptions.PollingIntervalSeconds));

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _biometricOptions.PollingIntervalSeconds)), stoppingToken);
        }
    }
}

